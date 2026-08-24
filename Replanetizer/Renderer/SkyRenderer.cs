// Copyright (C) 2018-2023, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Replanetizer.MemoryHook;
using Replanetizer.Tools;
using Replanetizer.Utils;

namespace Replanetizer.Renderer
{
    public class SkyRenderer : Renderer
    {
        private SkyboxModel? sky;
        private BufferContainer? container;
        private readonly ShaderTable shaderTable;
        private List<Texture> textures;
        private Dictionary<Texture, GLTexture> textureIds;
        private SkyboxMemoryState skyboxMemoryState = new SkyboxMemoryState();

        public SkyRenderer(ShaderTable shaderTable, List<Texture> textures, Dictionary<Texture, GLTexture> textureIds)
        {
            this.shaderTable = shaderTable;
            this.textureIds = textureIds;
            this.textures = textures;
        }

        public override void Include<T>(T obj)
        {
            if (obj is SkyboxModel sky)
            {
                this.sky = sky;
                container = new BufferContainer(sky, () =>
                {
                    GLUtil.ActivateNumberOfVertexAttribArrays(3);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 6, 0);
                    GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeof(float) * 6, sizeof(float) * 3);
                    GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(float) * 6, sizeof(float) * 5);
                });

                return;
            }
            throw new NotImplementedException();
        }

        public override void Include<T>(List<T> list) => throw new NotImplementedException();

        public void UpdateMemoryState(SkyboxMemoryState state)
        {
            skyboxMemoryState.CopyFrom(state);
        }

        public override void Render(RendererPayload payload)
        {
            if (sky == null || container == null) return;

            shaderTable.skyShader.UseShader();

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.Disable(EnableCap.DepthTest);

            Matrix4 view = payload.camera.GetViewMatrix().ClearTranslation();
            Matrix4 projection = payload.camera.GetProjectionMatrix();

            container.Bind();
            if (sky.textureConfigs.Count == 0)
            {
                RenderTextureConfigs(sky.textureConfig, view, projection, Matrix4.Identity);
            }
            else
            {
                for (int i = 0; i < sky.textureConfigs.Count; i++)
                {
                    Matrix4 layerRotation = Matrix4.CreateRotationZ(skyboxMemoryState.layerRotations[i]);

                    RenderTextureConfigs(sky.textureConfigs[i], view, projection, layerRotation);
                }
            }

            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GLUtil.CheckGlError("SkyRenderer");
        }

        private void RenderTextureConfigs(
            List<TextureConfig> configs,
            Matrix4 view,
            Matrix4 projection,
            Matrix4 layerRotation)
        {
            Matrix4 mvp = view * layerRotation * projection;
            shaderTable.skyShader.SetUniformMatrix4(UniformName.worldToView, ref mvp);

            foreach (TextureConfig conf in configs)
            {
                shaderTable.skyShader.SetUniform1(UniformName.texAvailable, (conf.id > 0) ? 1.0f : 0.0f);
                if (conf.id > 0)
                {
                    GLTexture tex = textureIds[textures[conf.id]];
                    tex.SetWrapModes(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
                    tex.Bind();
                }
                else
                {
                    GLTexture.BindNull();
                }

                GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
            }
        }

        public override void Dispose()
        {
            container?.Dispose();
        }
    }
}
