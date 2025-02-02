﻿using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using RatchetEdit.Tools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using static LibReplanetizer.Utilities;

namespace RatchetEdit
{
    public class CustomGLControl : GLControl
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public Level level { get; set; }

        public Matrix4 worldView { get; set; }

        public int shaderID { get; set; }
        public int colorShaderID { get; set; }
        public int collisionShaderID { get; set; }
        public int matrixID { get; set; }
        public int colorID { get; set; }

        private Matrix4 projection { get; set; }
        private Matrix4 view { get; set; }

        private int currentSplineVertex;
        public LevelObject selectedObject;

        private Vector3 prevMouseRay;
        private int lastMouseX, lastMouseY;
        private bool xLock, yLock, zLock, rMouse, lMouse;

        public bool initialized, invalidate;
        public bool enableMoby, enableTie, enableShrub, enableSpline,
            enableCuboid, enableType0C, enableSkybox, enableTerrain, enableCollision;

        public Camera camera;
        private Tool currentTool;
        public Tool translateTool, rotationTool, scalingTool, vertexTranslator;

        public event EventHandler<RatchetEventArgs> ObjectClick;
        public event EventHandler<RatchetEventArgs> ObjectDeleted;

        private ConditionalWeakTable<IRenderable, BufferContainer> bufferTable;
        public Dictionary<Texture, int> textureIds;

        MemoryHook hook;

        private int collisionVbo, collisionIbo = 0;

        public CustomGLControl()
        {
            bufferTable = new ConditionalWeakTable<IRenderable, BufferContainer>();
            InitializeComponent();
        }

        private void CustomGLControl_Load(object sender, EventArgs e)
        {
            // The designer crashes if when using GL calls in load
            if (DesignMode) return;

            MakeCurrent();

            GL.GenVertexArrays(1, out int VAO);
            GL.BindVertexArray(VAO);

            //Setup openGL variables
            GL.ClearColor(Color.SkyBlue);
            GL.Enable(EnableCap.DepthTest);
            GL.LineWidth(5.0f);

            //Setup general shader
            shaderID = GL.CreateProgram();
            LoadShader("Shaders/vs.glsl", ShaderType.VertexShader, shaderID);
            LoadShader("Shaders/fs.glsl", ShaderType.FragmentShader, shaderID);
            GL.LinkProgram(shaderID);

            //Setup color shader
            colorShaderID = GL.CreateProgram();
            LoadShader("Shaders/colorshadervs.glsl", ShaderType.VertexShader, colorShaderID);
            LoadShader("Shaders/colorshaderfs.glsl", ShaderType.FragmentShader, colorShaderID);
            GL.LinkProgram(colorShaderID);

            //Setup color shader
            collisionShaderID = GL.CreateProgram();
            LoadShader("Shaders/collisionshadervs.glsl", ShaderType.VertexShader, collisionShaderID);
            LoadShader("Shaders/collisionshaderfs.glsl", ShaderType.FragmentShader, collisionShaderID);
            GL.LinkProgram(collisionShaderID);

            matrixID = GL.GetUniformLocation(shaderID, "MVP");
            colorID = GL.GetUniformLocation(colorShaderID, "incolor");

            projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3, (float)Width / Height, 0.1f, 800.0f);

            camera = new Camera();

            translateTool = new TranslationTool();
            rotationTool = new RotationTool();
            scalingTool = new ScalingTool();
            vertexTranslator = new VertexTranslationTool();

            initialized = true;
        }

        void LoadLevelTextures()
        {
            textureIds = new Dictionary<Texture, int>();
            foreach (Texture t in level.textures)
            {
                int texId = 0;
                GL.GenTextures(1, out texId);
                GL.BindTexture(TextureTarget.Texture2D, texId);
                int offset = 0;

                if (t.mipMapCount > 1)
                {
                    int mipWidth = t.width;
                    int mipHeight = t.height;

                    for (int mipLevel = 0; mipLevel < t.mipMapCount; mipLevel++)
                    {
                        if (mipWidth > 0 && mipHeight > 0)
                        {
                            int size = ((mipWidth + 3) / 4) * ((mipHeight + 3) / 4) * 16;
                            byte[] texPart = new byte[size];
                            Array.Copy(t.data, offset, texPart, 0, size);
                            GL.CompressedTexImage2D(TextureTarget.Texture2D, mipLevel, InternalFormat.CompressedRgbaS3tcDxt5Ext, mipWidth, mipHeight, 0, size, texPart);
                            offset += size;
                            mipWidth /= 2;
                            mipHeight /= 2;
                        }
                    }
                }
                else
                {
                    int size = ((t.width + 3) / 4) * ((t.height + 3) / 4) * 16;
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.CompressedRgbaS3tcDxt5Ext, t.width, t.height, 0, size, t.data);
                    GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                }

                textureIds.Add(t, texId);
            }
        }

        void LoadCollisionBOs()
        {
            Collision col = (Collision) level.collisionModel;
            GL.GenBuffers(1, out collisionVbo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, collisionVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, col.vertexBuffer.Length * sizeof(float), col.vertexBuffer, BufferUsageHint.StaticDraw);

            GL.GenBuffers(1, out collisionIbo);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, collisionIbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, col.indBuff.Length * sizeof(int), col.indBuff, BufferUsageHint.StaticDraw);
        }

        public void LoadLevel(Level level)
        {
            this.level = level;
            LoadLevelTextures();
            LoadCollisionBOs();
            enableMoby = true;
            enableTie = true;
            enableShrub = true;
            enableTerrain = true;

            Moby ratchet = level.mobs[0];

            camera.MoveBehind(ratchet);

            SelectObject(null);
            hook = new MemoryHook(level.game.num);
        }

        public void SelectObject(LevelObject newObject = null)
        {
            if (newObject == null)
            {
                selectedObject = null;
                InvalidateView();
                return;
            }

            if ((selectedObject is Spline) && !(newObject is Spline))
            {
                //Previous object was spline, new isn't
                if (currentTool is VertexTranslationTool) SelectTool(null);
            }

            selectedObject = newObject;

            ObjectClick?.Invoke(this, new RatchetEventArgs
            {
                Object = newObject
            });

            InvalidateView();
        }

        public void DeleteObject(LevelObject levelObject)
        {
            SelectObject(null);
            ObjectDeleted?.Invoke(this, new RatchetEventArgs
            {
                Object = levelObject
            });
            InvalidateView();
        }

        private void CustomGLControl_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (!(selectedObject is Spline spline)) return;
            if (!(currentTool is VertexTranslationTool)) return;

            int delta = e.Delta / 120;
            if (delta > 0)
            {
                if (currentSplineVertex < spline.GetVertexCount() - 1)
                {
                    currentSplineVertex += 1;
                }
            }
            else if (currentSplineVertex > 0)
            {
                currentSplineVertex -= 1;
            }
            InvalidateView();
        }

        public void CloneMoby(Moby moby)
        {
            if (!(moby.Clone() is Moby newMoby)) return;

            level.mobs.Add(newMoby);
            SelectObject(newMoby);
            InvalidateView();
        }

        private void CustomGLControl_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.D1:
                    SelectTool(translateTool);
                    break;
                case Keys.D2:
                    SelectTool(rotationTool);
                    break;
                case Keys.D3:
                    SelectTool(scalingTool);
                    break;
                case Keys.D4:
                    SelectTool(vertexTranslator);
                    break;
                case Keys.D5:
                    SelectTool();
                    break;
                case Keys.Delete:
                    DeleteObject(selectedObject);
                    break;
            }
        }


        public void SelectTool(Tool tool = null)
        {
            //enableTranslateTool = (tool is TranslationTool);
            //enableRotateTool = (tool is RotationTool);
            //enableScaleTool = (tool is ScalingTool);
            //enableSplineTool = (tool is VertexTranslationTool);

            currentTool = tool;

            currentSplineVertex = 0;
            InvalidateView();
        }

        public void Tick()
        {
            float deltaTime = 0.016f;
            float moveSpeed = ModifierKeys == Keys.Shift ? 40 : 10;

            if (rMouse)
            {
                camera.rotation.Z -= (Cursor.Position.X - lastMouseX) * camera.speed * 0.016f;
                camera.rotation.X -= (Cursor.Position.Y - lastMouseY) * camera.speed * 0.016f;
                camera.rotation.X = MathHelper.Clamp(camera.rotation.X, MathHelper.DegreesToRadians(-89.9f), MathHelper.DegreesToRadians(89.9f));
                InvalidateView();
            }

            Vector3 moveDir = GetInputAxes();
            if (moveDir.Length > 0)
            {
                moveDir *= moveSpeed * deltaTime;
                InvalidateView();
            }
            camera.Translate(Vector3.Transform(moveDir, camera.GetRotationMatrix()));

            view = camera.GetViewMatrix();

            Vector3 mouseRay = MouseToWorldRay(projection, view, new Size(Width, Height), new Vector2(Cursor.Position.X, Cursor.Position.Y));

            if (xLock || yLock || zLock)
            {
                Vector3 direction = Vector3.Zero;
                if (xLock) direction = Vector3.UnitX;
                else if (yLock) direction = Vector3.UnitY;
                else if (zLock) direction = Vector3.UnitZ;
                float magnitudeMultiplier = 20;
                Vector3 magnitude = (mouseRay - prevMouseRay) * magnitudeMultiplier;


                switch (currentTool)
                {
                    case TranslationTool t:
                        selectedObject.Translate(direction * magnitude);
                        break;
                    case RotationTool t:
                        selectedObject.Rotate(direction * magnitude);
                        break;
                    case ScalingTool t:
                        selectedObject.Scale(direction * magnitude + Vector3.One);
                        break;
                    case VertexTranslationTool t:
                        if (selectedObject is Spline spline)
                        {
                            /*spline.TranslateVertex(currentSplineVertex, direction * magnitude);
                            //write at 0x346BA1180 + 0xC0 + spline.offset + currentSplineVertex * 0x10;
                            // List of splines 0x300A51BE0

                            byte[] ptrBuff = new byte[0x04];
                            int bytesRead = 0;
                            ReadProcessMemory(processHandle, 0x300A51BE0 + level.splines.IndexOf(spline) * 0x04, ptrBuff, ptrBuff.Length, ref bytesRead);
                            long splinePtr = ReadUint(ptrBuff, 0) + 0x300000010;

                            byte[] buff = new byte[0x0C];
                            Vector3 vec = spline.GetVertex(currentSplineVertex);
                            WriteFloat(buff, 0x00, vec.X);
                            WriteFloat(buff, 0x04, vec.Y);
                            WriteFloat(buff, 0x08, vec.Z);

                            WriteProcessMemory(processHandle, splinePtr + currentSplineVertex * 0x10, buff, buff.Length, ref bytesRead);*/
                        }
                        break;
                }

                InvalidateView();
            }

            prevMouseRay = mouseRay;
            lastMouseX = Cursor.Position.X;
            lastMouseY = Cursor.Position.Y;

            if (invalidate)
            {
                Invalidate();
                //invalidate = false;
            }
        }

        private Vector3 GetInputAxes()
        {
            KeyboardState keyState = Keyboard.GetState();

            float xAxis = 0, yAxis = 0, zAxis = 0;

            if (Focused)
            {
                if (keyState.IsKeyDown(Key.W)) yAxis++;
                if (keyState.IsKeyDown(Key.S)) yAxis--;
                if (keyState.IsKeyDown(Key.A)) xAxis--;
                if (keyState.IsKeyDown(Key.D)) xAxis++;
                if (keyState.IsKeyDown(Key.Q)) zAxis--;
                if (keyState.IsKeyDown(Key.E)) zAxis++;
            }


            return new Vector3(xAxis, yAxis, zAxis);
        }

        public void FakeDrawSplines(List<Spline> splines, int offset)
        {
            for (int i = 0; i < splines.Count; i++)
            {
                Spline spline = splines[i];
                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                Matrix4 worldView = this.worldView;
                GL.UniformMatrix4(matrixID, false, ref worldView);
                this.worldView = worldView;

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(spline);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
                GL.DrawArrays(PrimitiveType.LineStrip, 0, spline.vertexBuffer.Length / 3);
            }
        }
        public void FakeDrawCuboids(List<Cuboid> cuboids, int offset)
        {
            for (int i = 0; i < cuboids.Count; i++)
            {
                Cuboid cuboid = cuboids[i];

                GL.UseProgram(colorShaderID);
                GL.EnableVertexAttribArray(0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                Matrix4 mvp = cuboid.modelMatrix * worldView;
                GL.UniformMatrix4(matrixID, false, ref mvp);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));

                ActivateBuffersForModel(cuboid);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                GL.DrawElements(PrimitiveType.Triangles, Cuboid.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }
        public void FakeDrawObjects(List<ModelObject> levelObjects, int offset)
        {
            for (int i = 0; i < levelObjects.Count; i++)
            {
                ModelObject levelObject = levelObjects[i];

                if (levelObject.model == null || levelObject.model.vertexBuffer == null)
                    continue;

                Matrix4 mvp = levelObject.modelMatrix * worldView;  //Has to be done in this order to work correctly
                GL.UniformMatrix4(matrixID, false, ref mvp);

                ActivateBuffersForModel(levelObject.model);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);

                byte[] cols = BitConverter.GetBytes(i + offset);
                GL.Uniform4(colorID, new Vector4(cols[0] / 255f, cols[1] / 255f, cols[2] / 255f, 1));
                GL.DrawElements(PrimitiveType.Triangles, levelObject.model.indexBuffer.Length, DrawElementsType.UnsignedShort, 0);

            }
        }

        public void ActivateBuffersForModel(IRenderable renderable)
        {
            BufferContainer container = bufferTable.GetValue(renderable, BufferContainer.FromRenderable);
            container.Bind();
        }

        public void RenderTool()
        {
            // Render tool on top of everything
            GL.Clear(ClearBufferMask.DepthBufferBit);

            if ((selectedObject != null) && (currentTool != null))
            {
                if ((currentTool is VertexTranslationTool) && (selectedObject is Spline spline))
                {
                    currentTool.Render(spline.GetVertex(currentSplineVertex), this);
                }
                else
                {
                    currentTool.Render(selectedObject.position, this);
                }
            }
        }

        void LoadShader(string filename, ShaderType type, int program)
        {
            int address = GL.CreateShader(type);
            using (StreamReader sr = new StreamReader(filename))
            {
                GL.ShaderSource(address, sr.ReadToEnd());
            }
            GL.CompileShader(address);
            GL.AttachShader(program, address);
            Logger.Debug("Compiled shader from {0}, log: {1}", filename, GL.GetShaderInfoLog(address));
        }

        protected override void OnResize(EventArgs e)
        {
            if (DesignMode) { base.OnResize(e); return; }

            base.OnResize(e);
            if (!initialized) return;
            GL.Viewport(0, 0, Width, Height);
            projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 3, (float)Width / Height, 0.1f, 800.0f);

        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // CustomGLControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Name = "CustomGLControl";
            this.Load += new System.EventHandler(this.CustomGLControl_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.CustomGLControl_KeyDown);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.CustomGLControl_MouseDown);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.CustomGLControl_MouseUp);
            this.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.CustomGLControl_MouseWheel);
            this.ResumeLayout(false);

        }

        private void CustomGLControl_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            rMouse = e.Button == MouseButtons.Right;
            lMouse = e.Button == MouseButtons.Left;

            if (e.Button == MouseButtons.Left && level != null)
            {
                LevelObject obj = GetObjectAtScreenPosition(e.Location.X, e.Location.Y, out bool cancelSelection);

                if (cancelSelection) return;

                SelectObject(obj);
            }
        }

        private void CustomGLControl_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            rMouse = false;
            lMouse = false;
            xLock = false;
            yLock = false;
            zLock = false;
        }

        public LevelObject GetObjectAtScreenPosition(int x, int y, out bool hitTool)
        {
            LevelObject returnObject = null;
            int mobyOffset = 0, tieOffset = 0, shrubOffset = 0, splineOffset = 0, cuboidOffset = 0, tfragOffset = 0;
            MakeCurrent();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.UseProgram(colorShaderID);
            GL.EnableVertexAttribArray(0);
            GL.ClearColor(0, 0, 0, 0);

            worldView = view * projection;

            int offset = 0;


            if (enableMoby)
            {
                mobyOffset = offset;
                FakeDrawObjects(level.mobs.Cast<ModelObject>().ToList(), mobyOffset);
                offset += level.mobs.Count;
            }

            if (enableTie)
            {
                tieOffset = offset;
                FakeDrawObjects(level.ties.Cast<ModelObject>().ToList(), tieOffset);
                offset += level.ties.Count;
            }

            if (enableShrub)
            {
                shrubOffset = offset;
                FakeDrawObjects(level.shrubs.Cast<ModelObject>().ToList(), shrubOffset);
                offset += level.shrubs.Count;
            }

            if (enableSpline)
            {
                splineOffset = offset;
                FakeDrawSplines(level.splines, splineOffset);
                offset += level.splines.Count;
            }

            if (enableCuboid)
            {
                cuboidOffset = offset;
                FakeDrawCuboids(level.cuboids, cuboidOffset);
                offset += level.cuboids.Count;
            }

            if (enableTerrain)
            {
                tfragOffset = offset;
                FakeDrawObjects(level.terrains.Cast<ModelObject>().ToList(), tfragOffset);
                offset += level.cuboids.Count;
            }

            RenderTool();

            Pixel pixel = new Pixel();
            GL.ReadPixels(x, Height - y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, ref pixel);

            Logger.Trace("R: {0}, G: {1}, B: {2}, A: {3}", pixel.R, pixel.G, pixel.B, pixel.A);

            GL.ClearColor(Color.SkyBlue);

            // Some GPU's put the alpha at 0, others at 255
            if (pixel.A == 255 || pixel.A == 0)
            {
                pixel.A = 0;

                bool didHitTool = false;
                if (pixel.R == 255 && pixel.G == 0 && pixel.B == 0)
                {
                    didHitTool = true;
                    xLock = true;
                }
                else if (pixel.R == 0 && pixel.G == 255 && pixel.B == 0)
                {
                    didHitTool = true;
                    yLock = true;
                }
                else if (pixel.R == 0 && pixel.G == 0 && pixel.B == 255)
                {
                    didHitTool = true;
                    zLock = true;
                }

                if (didHitTool)
                {
                    InvalidateView();
                    hitTool = true;
                    return null;
                }



                int id = (int)pixel.ToUInt32();
                if (enableMoby && id < level.mobs?.Count)
                {
                    returnObject = level.mobs[id];
                }
                else if (enableTie && id - tieOffset < level.ties.Count)
                {
                    returnObject = level.ties[id - tieOffset];
                }
                else if (enableShrub && id - shrubOffset < level.shrubs.Count)
                {
                    returnObject = level.shrubs[id - shrubOffset];
                }
                else if (enableSpline && id - splineOffset < level.splines.Count)
                {
                    returnObject = level.splines[id - splineOffset];
                }
                else if (enableCuboid && id - cuboidOffset < level.cuboids.Count)
                {
                    returnObject = level.cuboids[id - cuboidOffset];
                }
                else if (enableTerrain && id - tfragOffset < level.terrains.Count)
                {
                    returnObject = level.terrains[id - tfragOffset];
                }
            }

            hitTool = false;
            return returnObject;
        }


        void InvalidateView()
        {
            invalidate = true;
        }

        void RenderModelObject(ModelObject modelObject, bool selected)
        {
            if (modelObject.model == null || modelObject.model.vertexBuffer == null || modelObject.model.textureConfig.Count == 0) return;
            Matrix4 mvp = modelObject.modelMatrix * worldView;  //Has to be done in this order to work correctly
            GL.UniformMatrix4(matrixID, false, ref mvp);
            ActivateBuffersForModel(modelObject);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);

            //Bind textures one by one, applying it to the relevant vertices based on the index array
            foreach (TextureConfig conf in modelObject.model.textureConfig)
            {
                GL.BindTexture(TextureTarget.Texture2D, (conf.ID > 0) ? textureIds[level.textures[conf.ID]] : 0);
                GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
            }

            if (selected)
            {
                GL.UseProgram(colorShaderID);
                GL.Uniform4(colorID, new Vector4(1, 1, 1, 1));
                GL.UniformMatrix4(matrixID, false, ref mvp);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.DrawElements(PrimitiveType.Triangles, modelObject.model.indexBuffer.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.UseProgram(shaderID);
            }

        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (DesignMode) { return; }
            Logger.Trace("Painting");

            worldView = view * projection;
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);


            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);

            MakeCurrent();

            GL.UseProgram(shaderID);

            if (enableMoby)
            {
                hook.UpdateMobys(level.mobs, level.mobyModels);

                foreach (Moby mob in level.mobs)
                {
                    RenderModelObject(mob, mob == selectedObject);
                }
            }


            if (enableTie)
                foreach (Tie tie in level.ties)
                    RenderModelObject(tie, tie == selectedObject);

            if (enableShrub)
                foreach (Shrub shrub in level.shrubs)
                    RenderModelObject(shrub, shrub == selectedObject);

            if (enableTerrain)
                foreach (TerrainFragment tFrag in level.terrains)
                    RenderModelObject(tFrag, tFrag == selectedObject);

            if (enableSkybox)
                foreach (TextureConfig conf in level.skybox.textureConfig)
                {
                    GL.BindTexture(TextureTarget.Texture2D, (conf.ID > 0) ? textureIds[level.textures[conf.ID]] : 0);
                    GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
                }

            GL.UseProgram(colorShaderID);

            if (enableSpline)
                foreach (Spline spline in level.splines)
                {
                    var worldView = this.worldView;
                    GL.UniformMatrix4(matrixID, false, ref worldView);
                    GL.Uniform4(colorID, spline == selectedObject ? LevelObject.selectedColor : LevelObject.normalColor);
                    ActivateBuffersForModel(spline);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
                    GL.DrawArrays(PrimitiveType.LineStrip, 0, spline.vertexBuffer.Length / 3);
                }

            if (enableCuboid)
                foreach (Cuboid cuboid in level.cuboids)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = cuboid.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, selectedObject == cuboid ? LevelObject.selectedColor : LevelObject.normalColor);
                    ActivateBuffersForModel(cuboid);
                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
                    GL.DrawElements(PrimitiveType.Triangles, Cuboid.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableType0C)
                foreach (Type0C type0c in level.type0Cs)
                {
                    GL.UseProgram(colorShaderID);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    Matrix4 mvp = type0c.modelMatrix * worldView;
                    GL.UniformMatrix4(matrixID, false, ref mvp);
                    GL.Uniform4(colorID, type0c == selectedObject ? LevelObject.selectedColor : LevelObject.normalColor);

                    ActivateBuffersForModel(type0c);

                    GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

                    GL.DrawElements(PrimitiveType.Triangles, Type0C.cubeElements.Length, DrawElementsType.UnsignedShort, 0);
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }

            if (enableCollision)
            {
                Collision col = (Collision)level.collisionModel;

                Matrix4 worldView = this.worldView;
                GL.UniformMatrix4(matrixID, false, ref worldView);
                GL.Uniform4(colorID, new Vector4(1, 1, 1, 1));

                GL.BindBuffer(BufferTarget.ArrayBuffer, collisionVbo);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 4, 0);
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, false, sizeof(float) * 4, sizeof(float) * 3);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, collisionIbo);

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.DrawElements(PrimitiveType.Triangles, col.indBuff.Length, DrawElementsType.UnsignedInt, 0);
                GL.UseProgram(collisionShaderID);
                GL.UniformMatrix4(matrixID, false, ref worldView);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.DrawElements(PrimitiveType.Triangles, col.indBuff.Length, DrawElementsType.UnsignedInt, 0);
            }

            RenderTool();

            GL.DisableVertexAttribArray(0);
            GL.DisableVertexAttribArray(1);

            SwapBuffers();
        }
    }

    public class RatchetEventArgs : EventArgs
    {
        public LevelObject Object { get; set; }
    }

}
