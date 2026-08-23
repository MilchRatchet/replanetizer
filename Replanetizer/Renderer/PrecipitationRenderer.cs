using System;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace Replanetizer.Renderer
{
    public class PrecipitationRenderer : Renderer
    {
        private const int PARTICLE_COUNT = 512;
        private const float SPAWN_RADIUS = 32.0f;
        private const float SPAWN_BOTTOM = 8.0f;
        private const float SPAWN_HEIGHT = 36.0f;
        private const float MIN_SPEED = 18.0f;
        private const float MAX_SPEED = 30.0f;
        private const float MIN_LENGTH = 0.7f;
        private const float MAX_LENGTH = 1.4f;
        private const float MIN_WIDTH = 0.035f;
        private const float MAX_WIDTH = 0.07f;
        private const float MAP_CELL_SIZE = 1.0f;

        private readonly ShaderTable shaderTable;
        private readonly PrecipitationMap map;
        private readonly Vector2 mapOrigin;
        private readonly Random random = new Random(0x52414331);
        private readonly Particle[] particles = new Particle[PARTICLE_COUNT];
        private readonly float[] instanceData = new float[PARTICLE_COUNT * 5];
        private readonly int vao;
        private readonly int quadVbo;
        private readonly int instanceVbo;
        private readonly int indexBuffer;
        private bool initialized;
        private bool fogEnabled;
        private Vector4 fogColor;
        private Vector4 fogParams;

        private struct Particle
        {
            public Vector3 position;
            public float speed;
            public float length;
            public float width;
        }

        public PrecipitationRenderer(ShaderTable shaderTable, PrecipitationMap map, Vector2 mapOrigin)
        {
            this.shaderTable = shaderTable;
            this.map = map;
            this.mapOrigin = mapOrigin;

            GLUtil.CreateVertexArray("Precipitation", out vao);
            GL.BindVertexArray(vao);

            float[] quadVertices =
            {
                -0.5f, 0.0f, 0.0f, 0.0f,
                 0.5f, 0.0f, 1.0f, 0.0f,
                -0.5f, 1.0f, 0.0f, 1.0f,
                 0.5f, 1.0f, 1.0f, 1.0f
            };
            ushort[] indices = { 0, 1, 2, 1, 3, 2 };

            GLUtil.CreateVertexBuffer("Precipitation quad", out quadVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);

            GLUtil.CreateElementBuffer("Precipitation indices", out indexBuffer);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);

            GLUtil.CreateVertexBuffer("Precipitation instances", out instanceVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, instanceData.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, sizeof(float) * 4, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 4, sizeof(float) * 2);

            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, sizeof(float) * 5, 0);
            GL.VertexAttribDivisor(2, 1);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, sizeof(float) * 5, sizeof(float) * 3);
            GL.VertexAttribDivisor(3, 1);
        }

        public void SetFog(LevelVariables? levelVariables, bool enabled)
        {
            fogEnabled = enabled && levelVariables != null;
            if (!fogEnabled || levelVariables == null)
                return;

            fogColor = new Vector4(
                levelVariables.fogColor.R / 255.0f,
                levelVariables.fogColor.G / 255.0f,
                levelVariables.fogColor.B / 255.0f,
                1.0f);
            fogParams = new Vector4(
                levelVariables.fogNearDistance / 1024.0f,
                1024.0f / (levelVariables.fogFarDistance - levelVariables.fogNearDistance),
                1.0f - levelVariables.fogNearIntensity / 255.0f,
                1.0f - levelVariables.fogFarIntensity / 255.0f);
        }

        public override void Include<T>(T obj) => throw new NotImplementedException();

        public override void Include<T>(List<T> list) => throw new NotImplementedException();

        private float NextFloat(float min, float max)
        {
            return min + (float) random.NextDouble() * (max - min);
        }

        private void Respawn(ref Particle particle, Vector3 cameraPosition)
        {
            particle.position = new Vector3(
                cameraPosition.X + NextFloat(-SPAWN_RADIUS, SPAWN_RADIUS),
                cameraPosition.Y + NextFloat(-SPAWN_RADIUS, SPAWN_RADIUS),
                cameraPosition.Z + SPAWN_BOTTOM + NextFloat(0.0f, SPAWN_HEIGHT));
            particle.speed = NextFloat(MIN_SPEED, MAX_SPEED);
            particle.length = NextFloat(MIN_LENGTH, MAX_LENGTH);
            particle.width = NextFloat(MIN_WIDTH, MAX_WIDTH);
        }

        private bool TryGetHeight(Vector3 position, out float height)
        {
            int x = (int) MathF.Floor((position.X - mapOrigin.X) / MAP_CELL_SIZE);
            int y = (int) MathF.Floor((position.Y - mapOrigin.Y) / MAP_CELL_SIZE);
            if (x < 0 || y < 0 || x >= map.rowStride || y >= map.numColumns)
            {
                height = 0.0f;
                return false;
            }

            height = map.GetHeight(x, y);
            return true;
        }

        private void UpdateParticles(RendererPayload payload)
        {
            float deltaTime = Math.Clamp(payload.deltaTime, 0.0f, 0.1f);
            Vector3 cameraPosition = payload.camera.position;

            for (int i = 0; i < particles.Length; i++)
            {
                ref Particle particle = ref particles[i];
                if (!initialized)
                {
                    Respawn(ref particle, cameraPosition);
                }
                else
                {
                    particle.position.Z -= particle.speed * deltaTime;
                    bool reachedSurface = TryGetHeight(particle.position, out float height) && particle.position.Z <= height;
                    bool leftVolume = MathF.Abs(particle.position.X - cameraPosition.X) > SPAWN_RADIUS * 1.5f ||
                                      MathF.Abs(particle.position.Y - cameraPosition.Y) > SPAWN_RADIUS * 1.5f ||
                                      particle.position.Z < cameraPosition.Z - 4.0f;
                    if (reachedSurface || leftVolume)
                    {
                        Respawn(ref particle, cameraPosition);
                    }
                }

                int offset = i * 5;
                instanceData[offset + 0] = particle.position.X;
                instanceData[offset + 1] = particle.position.Y;
                instanceData[offset + 2] = particle.position.Z;
                instanceData[offset + 3] = particle.width;
                instanceData[offset + 4] = particle.length;
            }

            initialized = true;
        }

        public override void Render(RendererPayload payload)
        {
            if (map.rowStride <= 0 || map.numColumns <= 0)
                return;

            UpdateParticles(payload);

            Matrix4 worldToView = payload.camera.GetWorldViewMatrix();
            Vector3 right = new Vector3(worldToView[0, 0], worldToView[1, 0], worldToView[2, 0]).Normalized();

            shaderTable.precipitationShader.UseShader();
            shaderTable.precipitationShader.SetUniformMatrix4(UniformName.worldToView, ref worldToView);
            shaderTable.precipitationShader.SetUniform3(UniformName.right, right);
            shaderTable.precipitationShader.SetUniform3(UniformName.worldDown, 0.0f, 0.0f, -1.0f);
            shaderTable.precipitationShader.SetUniform1(UniformName.useFog, fogEnabled && payload.visibility.enableFog ? 1 : 0);
            shaderTable.precipitationShader.SetUniform4(UniformName.fogColor, fogColor);
            shaderTable.precipitationShader.SetUniform4(UniformName.fogParams, fogParams);
            shaderTable.precipitationShader.SetUniform4(UniformName.particleColor, 0.72f, 0.84f, 1.0f, 0.38f);

            bool blendWasEnabled = GL.IsEnabled(EnableCap.Blend);
            bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
            bool depthMaskWasEnabled = GL.GetBoolean(GetPName.DepthWritemask);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.DrawBuffers(1, new[] { DrawBuffersEnum.ColorAttachment0 });

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, instanceData.Length * sizeof(float), instanceData);
            GL.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, IntPtr.Zero, PARTICLE_COUNT);

            GL.DrawBuffers(2, new[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1 });
            GL.DepthMask(depthMaskWasEnabled);
            if (!blendWasEnabled)
                GL.Disable(EnableCap.Blend);
            if (!depthWasEnabled)
                GL.Disable(EnableCap.DepthTest);
        }

        public override void Dispose()
        {
            GL.DeleteBuffer(indexBuffer);
            GL.DeleteBuffer(instanceVbo);
            GL.DeleteBuffer(quadVbo);
            GL.DeleteVertexArray(vao);
        }
    }
}
