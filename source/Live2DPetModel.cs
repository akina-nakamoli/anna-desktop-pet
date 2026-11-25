using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed class LiveFaceTracking
    {
        private readonly Random random;
        private double previousTime, blinkStart, closeDuration, holdDuration, openDuration;
        private bool performingUniform2;
        private double entryHeadX, entryEyeX, entryEyeY, entryEyeOpen;
        internal double HeadX { get; private set; }
        internal double EyeX { get; private set; }
        internal double EyeY { get; private set; }
        internal double EyeOpen { get; private set; }

        internal LiveFaceTracking(Random random)
        {
            this.random = random;
            Reset();
        }

        internal void Reset()
        {
            performingUniform2 = false;
            previousTime = -1;
            HeadX = EyeX = EyeY = 0;
            EyeOpen = 1;
        }

        internal static Vector Target(Point pointer, double width, double height, int frameWidth, int frameHeight, int topPadding = 0, double eyeHeight = .14, double eyeCenterX = .5)
        {
            if (width <= 0 || height <= 0 || frameWidth <= 0 || topPadding < 0 || frameHeight <= topPadding) return new Vector();
            double scale = Math.Min(width / frameWidth, height / frameHeight);
            double drawnWidth = frameWidth * scale, drawnHeight = frameHeight * scale;
            double eyesY = (height - drawnHeight) * .5 + (topPadding + (frameHeight-topPadding)*eyeHeight) * scale;
            double eyesX = (width - drawnWidth) * .5 + drawnWidth * eyeCenterX;
            return new Vector(Math.Tanh((pointer.X - eyesX) / (.65 * drawnWidth)),
                              Math.Tanh((eyesY - pointer.Y) / (.50 * scale * (frameHeight-topPadding))));
        }

        internal void Update(double seconds, double targetX, double targetY)
        {
            Update(seconds, targetX, targetY, new IdleGesturePose());
        }

        internal void Update(double seconds, double targetX, double targetY, IdleGesturePose gesture)
        {
            if (performingUniform2)
            {
                performingUniform2 = false;
                ScheduleBlink(seconds);
            }
            if (previousTime < 0 || seconds < previousTime || seconds - previousTime > .5)
            {
                previousTime = seconds;
                EyeOpen = 1;
                ScheduleBlink(seconds);
                return;
            }
            double dt = Math.Min(.08, seconds - previousTime);
            previousTime = seconds;
            // In idle the head trails the eyes. During an action, let the previous
            // mouse-driven turn settle while the gesture supplies its own head pose.
            const double eyeRate = 24, headRate = 6;
            double eyeDecay = Math.Exp(-eyeRate * dt), headDecay = Math.Exp(-headRate * dt);
            if (gesture.ControlsGaze) HeadX *= headDecay;
            else HeadX = targetX + (HeadX - targetX) * headDecay
                         + (EyeX - targetX) * headRate / (eyeRate - headRate) * (headDecay - eyeDecay);
            targetX = gesture.CombineEye(targetX);
            targetY = gesture.CombineEyeY(targetY);
            EyeX = targetX + (EyeX - targetX) * eyeDecay;
            EyeY = targetY + (EyeY - targetY) * eyeDecay;

            double phase = seconds - blinkStart;
            if (phase < 0) EyeOpen = 1;
            else if (phase < closeDuration) EyeOpen = 1 - SmoothStep(phase / closeDuration);
            else if (phase < closeDuration + holdDuration) EyeOpen = 0;
            else if (phase < closeDuration + holdDuration + openDuration)
                EyeOpen = SmoothStep((phase - closeDuration - holdDuration) / openDuration);
            else
            {
                EyeOpen = 1;
                ScheduleBlink(blinkStart + closeDuration + holdDuration + openDuration);
            }
        }

        internal void UpdateUniform2(double seconds, Uniform2GesturePose shot)
        {
            // Capture the last idle frame once. The character eases out of this
            // pose while ignoring further mouse movement for the whole take.
            if (!performingUniform2)
            {
                entryHeadX = HeadX;
                entryEyeX = EyeX;
                entryEyeY = EyeY;
                entryEyeOpen = EyeOpen;
                performingUniform2 = true;
            }
            previousTime = seconds;
            HeadX = shot.Turn + shot.EntryCarry*entryHeadX;
            EyeX = shot.Face.EyeLook + shot.EntryCarry*entryEyeX;
            EyeY = shot.Face.CombineEyeY(0) + shot.EntryCarry*entryEyeY;
            EyeOpen = 1 + shot.EntryCarry*(entryEyeOpen-1);
        }

        private void ScheduleBlink(double after)
        {
            blinkStart = after + 2.8 + 3.4 * random.NextDouble();
            closeDuration = .085 + .025 * random.NextDouble();
            holdDuration = .032 + .012 * random.NextDouble();
            openDuration = .14 + .045 * random.NextDouble();
        }

        private static double SmoothStep(double value)
        {
            return value * value * (3 - 2 * value);
        }
    }

    internal struct GhostFlamePose
    {
        internal double Left, Right;
        internal static GhostFlamePose At(double seconds)
        {
            return new GhostFlamePose { Left = Math.Sin(seconds * 2*Math.PI/4),
                Right = Math.Sin(seconds * 2*Math.PI/6 + 1.1) };
        }
    }

    // The shipped front-facing models use a single texture atlas and normal alpha blending.
    internal sealed class Live2DPetModel : IDisposable
    {
        internal const int PortraitHeight = 544;
        internal const int OverheadPadding = 45;
        private const string Core = "PurismCore.dll";
        [StructLayout(LayoutKind.Sequential)]
        private struct Point2 { public float X, Y; }
        [DllImport(Core)] private static extern int csmHasMocConsistency(IntPtr moc, uint size);
        [DllImport(Core)] private static extern IntPtr csmReviveMocInPlace(IntPtr moc, uint size);
        [DllImport(Core)] private static extern uint csmGetSizeofModel(IntPtr moc);
        [DllImport(Core)] private static extern IntPtr csmInitializeModelInPlace(IntPtr moc, IntPtr memory, uint size);
        [DllImport(Core)] private static extern void csmReadCanvasInfo(IntPtr model, out Point2 canvas, out Point2 origin, out float pixelsPerUnit);
        [DllImport(Core)] private static extern int csmGetParameterCount(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetParameterIds(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetParameterValues(IntPtr model);
        [DllImport(Core)] private static extern void csmUpdateModel(IntPtr model);
        [DllImport(Core)] private static extern int csmGetDrawableCount(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableIds(IntPtr model);
        [DllImport(Core)] private static extern int csmGetOffscreenCount(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableMaskCounts(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableTextureIndices(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableBlendModes(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableVertexCounts(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableVertexPositions(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableVertexUvs(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableIndexCounts(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableIndices(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetDrawableOpacities(IntPtr model);
        [DllImport(Core)] private static extern IntPtr csmGetRenderOrders(IntPtr model);

        private sealed class Mesh
        {
            public float[] Vertices, Uvs;
            public short[] Indices;
            public GestureVertex[] Gesture;
            public int FlameSide;
            public bool Mouth;
        }

        private struct GestureVertex
        {
            public float BalanceX, BalanceY;
            public float NeckInfluence;
            public float SmileX, SmileY;
            public float PitchY, PitchUpY;
            public float FlameTip, LooseHair, LooseHem, UpperBody;
            public float RoundLipX, RoundLipY;
        }

        private sealed class PortraitLandmarks
        {
            internal readonly Point Eyes, Mouth, Neck;
            private static Point Landmark(Dictionary<string,object> data, string name)
            {
                double x = Convert.ToDouble(data[name+"X"]), y = Convert.ToDouble(data[name+"Y"]);
                if (!(x > 0 && x < 1 && y > 0 && y < 1)) throw new InvalidDataException("立绘五官坐标超出画布。");
                return new Point(x,y);
            }
            internal PortraitLandmarks(Dictionary<string,object> data)
            {
                Eyes=Landmark(data,"Eye"); Mouth=Landmark(data,"Mouth"); Neck=Landmark(data,"Neck");
                if (!(Eyes.Y < Mouth.Y && Mouth.Y < Neck.Y)) throw new InvalidDataException("立绘五官坐标无效。");
            }
            internal GestureVertex Weights(double x,double y,string drawable)
            {
                // The frame is a single rigid accessory: pivot with the head, keep the
                // bridge level with both eyes, and never apply the skin's local smile.
                if (drawable == "Glasses") return GestureWeights(.502,.14,true);
                Point lower, upper;
                double near, far;
                if (y <= Eyes.Y) { lower = new Point(Eyes.X,0); upper=Eyes; near=0; far=.14; }
                else if (y <= Mouth.Y) { lower=Eyes; upper=Mouth; near=.14; far=.171; }
                else if (y <= Neck.Y) { lower=Mouth; upper=Neck; near=.171; far=.203; }
                else { lower=Neck; upper=new Point(Neck.X,1); near=.203; far=1; }
                double t=(y-lower.Y)/(upper.Y-lower.Y);
                return GestureWeights(x+.502-(lower.X+(upper.X-lower.X)*t), near+(far-near)*t,drawable=="Body");
            }
        }

        private IntPtr mocAllocation, modelAllocation, model, valuesPointer;
        private Point2 canvas, origin;
        private float ppu;
        private double lastAngle = double.NaN, lastX, lastY, lastLeftLid, lastRightLid, lastMouth;
        private GhostFlamePose lastFlames;
        private bool hadGhostDeformation;
        internal double OtterDozeTime = -1;
        private bool ghost;
        private readonly PortraitConversationMotion conversationMotion = new PortraitConversationMotion();
        private GhostGesturePose lastGhostAction;
        private PortraitLandmarks portrait;
        internal bool HasIndependentFlames { get { return ghost; } }
        internal bool HasPortraitRegistration { get { return portrait != null; } }
        internal bool HasGlasses { get; private set; }
        internal double EyeHeight { get { return portrait != null ? portrait.Eyes.Y : ghost ? .162 : .14; } }
        internal double EyeCenterX { get { return portrait != null ? portrait.Eyes.X : .5; } }
        private IdleGesturePose lastGesture;
        private readonly Dictionary<string,int> parameters = new Dictionary<string,int>();
        private float[] values, opacities;
        private int[] orders, sequence;
        private Mesh[] meshes;
        private int textureWidth, textureHeight;
        private byte[] texture;
        internal WriteableBitmap Frame { get; private set; }
        internal byte[] Pixels { get; private set; }

        internal static Live2DPetModel Load(string path)
        {
            var result = new Live2DPetModel();
            try { result.Initialize(path); return result; }
            catch { result.Dispose(); throw; }
        }

        private static string ModelFile(string manifest, string relative)
        {
            string directory = Path.GetFullPath(Path.GetDirectoryName(manifest)) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(directory, relative));
            if (!resolved.StartsWith(directory,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("模型资源路径超出模型目录。");
            return resolved;
        }

        private static IntPtr Allocate(int size, out IntPtr memory)
        {
            if (size <= 0 || size > 128 * 1024 * 1024) throw new InvalidDataException("模型内存大小无效。");
            memory = Marshal.AllocHGlobal(size + 63);
            return new IntPtr((memory.ToInt64() + 63) & ~63L);
        }

        private void Initialize(string path)
        {
            var manifest = (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));
            object portraitData;
            if (manifest.TryGetValue("AnnaPortrait",out portraitData))
                portrait = new PortraitLandmarks((Dictionary<string,object>)portraitData);
            var files = (Dictionary<string,object>)manifest["FileReferences"];
            var textures = (object[])files["Textures"];
            if (textures.Length != 1) throw new InvalidDataException("当前桌宠模型需要单张纹理图集。");
            var mocPath = ModelFile(path,(string)files["Moc"]);
            if (new FileInfo(mocPath).Length > 32 * 1024 * 1024) throw new InvalidDataException("模型过大。");
            byte[] bytes = File.ReadAllBytes(mocPath);
            IntPtr moc = Allocate(bytes.Length, out mocAllocation);
            Marshal.Copy(bytes,0,moc,bytes.Length);
            if (csmHasMocConsistency(moc,(uint)bytes.Length) == 0 || csmReviveMocInPlace(moc,(uint)bytes.Length) == IntPtr.Zero)
                throw new InvalidDataException("MOC3 校验失败。");
            uint size = csmGetSizeofModel(moc);
            model = csmInitializeModelInPlace(moc,Allocate(checked((int)size),out modelAllocation),size);
            if (model == IntPtr.Zero) throw new InvalidDataException("MOC3 初始化失败。");
            csmReadCanvasInfo(model,out canvas,out origin,out ppu);
            if (canvas.X <= 0 || canvas.Y <= 0 || ppu <= 0 || csmGetOffscreenCount(model) != 0) throw new InvalidDataException("不支持此模型画布。");
            values = new float[csmGetParameterCount(model)];
            IntPtr ids = csmGetParameterIds(model);
            valuesPointer = csmGetParameterValues(model);
            Marshal.Copy(valuesPointer,values,0,values.Length);
            for (int i=0;i<values.Length;i++) parameters[Marshal.PtrToStringAnsi(PointerAt(ids,i))]=i;
            ghost = parameters.ContainsKey("ParamFlameLeft") && parameters.ContainsKey("ParamFlameRight");
            using (Stream stream = File.OpenRead(ModelFile(path,(string)textures[0])))
            {
                BitmapSource image = new FormatConvertedBitmap(BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad),PixelFormats.Pbgra32,null,0);
                // Prefilter large illustrations so fine lashes and rim lines stay
                // continuous when sampled at desktop size.
                if ((ghost || portrait != null) && canvas.Y >= 2*PortraitHeight)
                    image = new TransformedBitmap(image, new ScaleTransform(.5,.5));
                textureWidth = image.PixelWidth; textureHeight = image.PixelHeight;
                texture = new byte[checked(textureWidth*textureHeight*4)];
                image.CopyPixels(texture,textureWidth*4,0);
            }
            meshes = new Mesh[csmGetDrawableCount(model)];
            csmUpdateModel(model);
            IntPtr restPositions = csmGetDrawableVertexPositions(model);
            IntPtr drawableIds = csmGetDrawableIds(model);
            orders = new int[meshes.Length]; sequence = new int[meshes.Length]; opacities = new float[meshes.Length];
            for (int i=0;i<meshes.Length;i++)
            {
                if (IntAt(csmGetDrawableMaskCounts(model),i)!=0 || IntAt(csmGetDrawableTextureIndices(model),i)!=0 || IntAt(csmGetDrawableBlendModes(model),i)!=0)
                    throw new InvalidDataException("模型使用了未支持的绘制模式。");
                int count = IntAt(csmGetDrawableVertexCounts(model),i);
                Mesh mesh = meshes[i] = new Mesh { Vertices=new float[2*count], Uvs=new float[2*count], Indices=new short[IntAt(csmGetDrawableIndexCounts(model),i)] };
                Marshal.Copy(PointerAt(csmGetDrawableVertexUvs(model),i),mesh.Uvs,0,mesh.Uvs.Length);
                Marshal.Copy(PointerAt(csmGetDrawableIndices(model),i),mesh.Indices,0,mesh.Indices.Length);
                for (int j=0;j<mesh.Uvs.Length;j+=2) { mesh.Uvs[j]*=textureWidth; mesh.Uvs[j+1]=(1-mesh.Uvs[j+1])*textureHeight; }
                string id = Marshal.PtrToStringAnsi(PointerAt(drawableIds,i));
                if (id == "Glasses") HasGlasses = true;
                mesh.FlameSide = id == "Flame_left" ? -1 : id == "Flame_right" ? 1 : 0;
                mesh.Mouth = id == "Mouth";
                if (ghost || id == "Body" || id == "Head_and_hair" || id == "Mouth" || id == "Glasses" ||
                    id == "Eye_screen_left" || id == "Eye_screen_right" || id == "Iris_screen_left" || id == "Iris_screen_right")
                {
                    Marshal.Copy(PointerAt(restPositions,i),mesh.Vertices,0,mesh.Vertices.Length);
                    mesh.Gesture = new GestureVertex[count];
                    for (int v=0;v<count;v++)
                    {
                        double x = (origin.X + mesh.Vertices[2*v]*ppu) / canvas.X;
                        double y = (origin.Y - mesh.Vertices[2*v+1]*ppu) / canvas.Y;
                        // Register face, neck and shoulders to the deformation profile;
                        // the kimono illustration's neck is lower on its canvas.
                        if (ghost)
                        {
                            double profileY = y < .236 ? y * .203/.236 : .203 + (y-.236)*.797/.764;
                            GestureVertex weights = mesh.FlameSide != 0 ? new GestureVertex() : GestureWeights(x+.002, profileY, id == "Body");
                            weights.FlameTip = (float)(1 - Ramp(.011,.080,y));
                            weights.UpperBody = (float)(1 - Ramp(.41,.73,y));
                            if (id == "Head_and_hair")
                                weights.LooseHair = (float)(Ramp(.14,.24,y) * Ramp(.052,.101,Math.Abs(x-.5)));
                            if (id == "Body")
                                weights.LooseHem = (float)(Ramp(.51,.76,y) * (1-Ramp(.899,.929,y)));
                            if (mesh.Mouth)
                            {
                                weights.RoundLipX = (float)(-.23*(x-513.0/1024));
                                weights.RoundLipY = (float)(.46*(y-310.0/1536));
                            }
                            mesh.Gesture[v] = weights;
                        }
                        else mesh.Gesture[v] = portrait != null ? portrait.Weights(x,y,id) : GestureWeights(x,y,id=="Body");
                    }
                }
            }
            const int height = PortraitHeight + OverheadPadding;
            int width = (int)Math.Round(PortraitHeight * canvas.X / canvas.Y);
            if (width<1 || width>2048) throw new InvalidDataException("模型尺寸无效。");
            Frame=new WriteableBitmap(width,height,96,96,PixelFormats.Pbgra32,null);
            Pixels=new byte[width*height*4];
        }

        private static IntPtr PointerAt(IntPtr address,int i) { return Marshal.ReadIntPtr(address,i*IntPtr.Size); }
        private static int IntAt(IntPtr address,int i) { return Marshal.ReadInt32(address,i*4); }
        private void Set(string id,float value) { int index; if(parameters.TryGetValue(id,out index)) values[index]=value; }

        private static double Ramp(double low, double high, double value)
        {
            double t = Math.Max(0, Math.Min(1, (value-low)/(high-low)));
            return t*t*(3-2*t);
        }

        private static double Region(double dx, double dy)
        {
            return Math.Exp(-dx*dx-dy*dy);
        }

        // Rest-space landmarks keep the facial layers registered when Cubism turns the head,
        // moves the irises, or opens the mouth. Displacements are fractions of the canvas.
        private static GestureVertex GestureWeights(double x, double y, bool body)
        {
            GestureVertex g = new GestureVertex();
            double aboveAnkles = 1-Ramp(.54,.83,y);
            double hips = Ramp(.21,.49,y);
            double shoulder = 1-Ramp(.31,.54,y);
            g.BalanceX = (float)(aboveAnkles * (.012 + .024*hips));
            g.BalanceY = (float)(aboveAnkles * (.003 + (x-.5)*(.044*shoulder-.052*hips)));
            // The neck and the clothing directly beneath the hair follow the same pivot.
            // The collar and shoulders gradually give way to a stationary rib cage.
            g.NeckInfluence = body ? (float)(1-Ramp(.215,.43,y)) : 1;
            // Pitch around the neck. The crown foreshortens and the front of the face
            // descends a little further than the fringe; the shoulders stay planted.
            double headDepth = Math.Max(0, .205 - y);
            double facialPlane = Region((x-.502)/.100, (y-.148)/.052);
            g.PitchY = (float)((1 - Ramp(.195,.220,y)) * (.092*headDepth + .0063*facialPlane));
            // The cowlick starts just below the canvas edge. Let the upper face
            // lift under it without pulling the tip out of the artwork.
            g.PitchUpY = (float)(g.PitchY * Ramp(.027,.087,y));
            if (body) return g;

            double fromCenter = x-.502;
            double side = Math.Sign(fromCenter), distance = Math.Abs(fromCenter);
            double lipCorner = Region((distance-.021)/.016, (y-.171)/.0105);
            double lowerLip = Region(distance/.011, (y-.175)/.0085);
            double cheek = Region((distance-.049)/.026, (y-.156)/.015);
            double outerLid = Region((distance-.063)/.023, (y-.142)/.012);
            g.SmileX = (float)(side * (.0052*lipCorner + .0023*cheek));
            g.SmileY = (float)(-.0084*lipCorner + .0012*lowerLip - .0038*cheek - .0021*outerLid);
            return g;
        }

        internal void Render(double headX,double gazeX,double gazeY,double eyeOpen,double mouth)
        {
            Render(headX,gazeX,gazeY,eyeOpen,mouth,new IdleGesturePose());
        }

        internal void Render(double headX,double gazeX,double gazeY,double eyeOpen,double mouth,
                             double weightShift,double listenTilt,double snackSmile)
        {
            var gesture = new IdleGesturePose(weightShift,listenTilt,snackSmile,0);
            gesture.ControlsGaze = weightShift != 0 || listenTilt != 0 || snackSmile != 0;
            Render(headX,gazeX,gazeY,eyeOpen,mouth,gesture);
        }

        internal void Render(double headX,double gazeX,double gazeY,double eyeOpen,double mouth,IdleGesturePose gesture,GhostFlamePose flames = default(GhostFlamePose))
        {
            RenderFrame(headX,gesture.CombineEye(gazeX),gesture.CombineEyeY(gazeY),eyeOpen,mouth,gesture,flames);
        }

        internal void Render(LiveFaceTracking face,double mouth,IdleGesturePose gesture,GhostFlamePose flames = default(GhostFlamePose))
        {
            RenderFrame(face.HeadX,face.EyeX,face.EyeY,face.EyeOpen,mouth,gesture,flames);
        }

        internal void ResetConversationMotion() { conversationMotion.Reset(); }
        internal double RenderedMouth { get { return lastMouth; } }

        internal void RenderTracked(LiveFaceTracking face, double seconds, Vector target, IdleGesturePose gesture,
            double mouth, bool animate, GhostGesturePose action = default(GhostGesturePose),
            Uniform2GesturePose uniform2 = default(Uniform2GesturePose),
            PortraitConversationState conversation = PortraitConversationState.None)
        {
            if (!animate) conversationMotion.Reset();
            else
            {
                var skin = ghost ? ConversationPortrait.Ghost : HasGlasses ? ConversationPortrait.Uniform2 : ConversationPortrait.Uniform1;
                GhostGesturePose previous=lastGhostAction, shot;
                previous.Face=lastGesture;
                if (conversationMotion.Update(skin,conversation,seconds,mouth,previous,uniform2,out shot))
                {
                    face.Update(seconds,0,0,shot.Face);
                    RenderFrame(face.HeadX,face.EyeX,face.EyeY,face.EyeOpen,
                        conversation==PortraitConversationState.Speaking ? mouth : 0,shot.Face,
                        ghost ? GhostFlamePose.At(seconds) : new GhostFlamePose(),shot);
                    return;
                }
            }
            if (animate && uniform2.Face.ControlsGaze && portrait != null && HasGlasses && !ghost)
            {
                face.UpdateUniform2(seconds,uniform2);
                RenderFrame(face.HeadX,face.EyeX,face.EyeY,face.EyeOpen,mouth,uniform2.Face,new GhostFlamePose());
                return;
            }
            if (!ghost || !animate) action = new GhostGesturePose();
            if (action.Face.ControlsGaze) gesture = action.Face;
            if (animate) face.Update(seconds, target.X, target.Y, gesture);
            else face.Reset();
            RenderFrame(face.HeadX,face.EyeX,face.EyeY,face.EyeOpen,animate ? mouth : 0,gesture,
                ghost && animate ? GhostFlamePose.At(seconds) : new GhostFlamePose(),action);
        }

        private void RenderFrame(double headX,double gazeX,double gazeY,double eyeOpen,double mouth,IdleGesturePose gesture,
            GhostFlamePose flames,GhostGesturePose action = default(GhostGesturePose))
        {
            double leftLid = gesture.ScreenLeftEyeOpen(eyeOpen)*(1-action.LeftEyeClosure);
            double rightLid = gesture.ScreenRightEyeOpen(eyeOpen)*(1-action.RightEyeClosure);
            mouth = Math.Max(mouth,action.MouthOpen);
            double weightShift = gesture.WeightShift, listenTilt = gesture.ListenTilt, snackSmile = gesture.SnackSmile;
            bool portraitWarp = action.Lift!=0 || action.Lean!=0 || action.HairWind!=0 || action.HemWind!=0 ||
                               action.HemLag!=0 || action.MouthOpen!=0;
            bool flameWarp = action.FlameLeftX!=0 || action.FlameLeftY!=0 || action.FlameLeftTip!=0 || action.FlameLeftDim!=0 ||
                             action.FlameRightX!=0 || action.FlameRightY!=0 || action.FlameRightTip!=0 || action.FlameRightDim!=0;
            double age = OtterDozeTime;
            bool otter = age >= 0 && age < 4;
            double nod = 0;
            if (otter)
            {
                double strength = Math.Min(1, age * 4) * Math.Min(1, (4 - age) * 4);
                nod = strength * Math.Min(1, age * .55) * Math.Min(1, Math.Max(0, (3.4 - age) * 5));
                leftLid *= 1 - .97 * nod;
                rightLid *= 1 - .97 * nod;
            }
            // Avoid rerasterizing a stationary face between blinks.
            if (!otter && !portraitWarp && !flameWarp && !hadGhostDeformation &&
                Math.Abs(headX-lastAngle)<.001 && Math.Abs(gazeX-lastX)<.001 && Math.Abs(gazeY-lastY)<.001 &&
                Math.Abs(leftLid-lastLeftLid)<.001 && Math.Abs(rightLid-lastRightLid)<.001 &&
                Math.Abs(mouth-lastMouth)<.001 && (!ghost || (Math.Abs(flames.Left-lastFlames.Left)<.003 &&
                Math.Abs(flames.Right-lastFlames.Right)<.003)) && gesture.MatchesGeometry(lastGesture)) return;
            lastAngle=headX; lastX=gazeX; lastY=gazeY; lastLeftLid=leftLid; lastRightLid=rightLid;
            lastMouth=mouth; lastGesture=gesture;
            lastGhostAction=action;
            lastFlames=flames;
            hadGhostDeformation=otter||portraitWarp||flameWarp;
            Set("ParamAngleX",(float)headX*30);
            Set("ParamEyeBallX",(float)gazeX); Set("ParamEyeBallY",(float)gazeY);
            // Cubism names left/right from the character's point of view.
            Set("ParamEyeROpen",(float)leftLid); Set("ParamEyeLOpen",(float)rightLid);
            Set("ParamMouthOpenY",(float)mouth);
            if (ghost) { Set("ParamFlameLeft",(float)flames.Left); Set("ParamFlameRight",(float)flames.Right); }
            Marshal.Copy(values,0,valuesPointer,values.Length);
            csmUpdateModel(model);
            Marshal.Copy(csmGetRenderOrders(model),orders,0,orders.Length);
            Marshal.Copy(csmGetDrawableOpacities(model),opacities,0,opacities.Length);
            for (int i=0;i<sequence.Length;i++)
            {
                sequence[i]=i;
                // Beside the head the layers never overlap. A light that moves
                // toward the cheek must pass in front of the fringe.
                if (ghost && meshes[i].FlameSide!=0) orders[i]+=sequence.Length;
            }
            Array.Sort(orders,sequence);
            Array.Clear(Pixels,0,Pixels.Length);
            IntPtr positions=csmGetDrawableVertexPositions(model);
            float sx=Frame.PixelWidth/canvas.X, sy=PortraitHeight/canvas.Y;
            bool gesturing = weightShift != 0 || listenTilt != 0 || snackSmile != 0 || gesture.HeadPitch != 0;
            double radians = listenTilt * (7.5*Math.PI/180);
            double sin = Math.Sin(radians), cosMinusOne = Math.Cos(radians)-1;
            double neckX = Frame.PixelWidth*(portrait != null ? portrait.Neck.X : ghost ? .5 : .502);
            double neckY = OverheadPadding + PortraitHeight*(portrait != null ? portrait.Neck.Y : ghost ? .236 : .203);
            double obiY = OverheadPadding + PortraitHeight*.39;
            foreach(int i in sequence)
            {
                if (opacities[i]<=0) continue;
                Mesh mesh=meshes[i];
                bool flame=mesh.FlameSide!=0, leftFlame=mesh.FlameSide<0;
                double fireX=leftFlame ? action.FlameLeftX : action.FlameRightX;
                double fireY=leftFlame ? action.FlameLeftY : action.FlameRightY;
                double tip=leftFlame ? action.FlameLeftTip : action.FlameRightTip;
                float dim=flame ? (float)(leftFlame ? action.FlameLeftDim : action.FlameRightDim) : 0;
                float opacity=opacities[i]*(1-.30f*Math.Max(0,dim));
                Marshal.Copy(PointerAt(positions,i),mesh.Vertices,0,mesh.Vertices.Length);
                for (int v=0;v<mesh.Vertices.Length;v+=2)
                {
                    mesh.Vertices[v]=(origin.X+mesh.Vertices[v]*ppu)*sx;
                    mesh.Vertices[v+1]=OverheadPadding+(origin.Y-mesh.Vertices[v+1]*ppu)*sy;
                    if (otter)
                    {
                        double y = (mesh.Vertices[v+1]-OverheadPadding)/PortraitHeight;
                        double head = Math.Max(0, Math.Min(1, (.35-y)/.15));
                        mesh.Vertices[v+1] += (float)(PortraitHeight * .016 * nod * head);
                    }
                    if (gesturing && !flame && mesh.Gesture != null)
                    {
                        GestureVertex g = mesh.Gesture[v/2];
                        double dx = mesh.Vertices[v]-neckX, dy = mesh.Vertices[v+1]-neckY;
                        mesh.Vertices[v] += (float)(g.NeckInfluence*(dx*cosMinusOne-dy*sin)
                                           + Frame.PixelWidth*(g.BalanceX*weightShift+g.SmileX*snackSmile));
                        mesh.Vertices[v+1] += (float)(g.NeckInfluence*(dx*sin+dy*cosMinusOne)
                                           + PortraitHeight*(g.BalanceY*weightShift+g.SmileY*snackSmile
                                                           +(gesture.HeadPitch<0?g.PitchUpY:g.PitchY)*gesture.HeadPitch));
                    }
                    if (flame && flameWarp)
                    {
                        // Bend the tip around the flame core; a gust or a sleepy
                        // droop does not shear the round glowing body below.
                        double curl=mesh.Gesture[v/2].FlameTip;
                        mesh.Vertices[v] += (float)(sx*(fireX + 24*tip*curl));
                        mesh.Vertices[v+1] += (float)(sy*(fireY + 9*tip*tip*curl));
                    }
                    else if (!flame && portraitWarp && mesh.Gesture != null)
                    {
                        GestureVertex g=mesh.Gesture[v/2];
                        // Lift moves the body as a single silhouette. Secondary
                        // deflection is restricted to loose hair and lower cloth;
                        // the wrists and hands on the obi share the torso motion.
                        float lean=(float)(action.Lean*g.UpperBody);
                        mesh.Vertices[v]+=(float)(.043*lean*(mesh.Vertices[v]-neckX) +
                            sx*(12*action.HairWind*g.LooseHair + 24*action.HemWind*g.LooseHem) +
                            Frame.PixelWidth*g.RoundLipX*action.MouthOpen);
                        mesh.Vertices[v+1]+=(float)(.030*lean*(mesh.Vertices[v+1]-obiY) +
                            sy*(-42*action.Lift + 14*action.HemLag*g.LooseHem) +
                            PortraitHeight*g.RoundLipY*action.MouthOpen);
                    }
                }
                for(int j=0;j<mesh.Indices.Length;j+=3)
                    DrawTriangle(mesh,2*(ushort)mesh.Indices[j],2*(ushort)mesh.Indices[j+1],2*(ushort)mesh.Indices[j+2],opacity,dim,Frame.PixelWidth,Frame.PixelHeight);
            }
            Frame.WritePixels(new Int32Rect(0,0,Frame.PixelWidth,Frame.PixelHeight),Pixels,Frame.PixelWidth*4,0);
        }

        private void DrawTriangle(Mesh mesh,int a,int b,int c,float opacity,float dim,int width,int height)
        {
            float[] v=mesh.Vertices, uv=mesh.Uvs;
            float ax=v[a],ay=v[a+1], ux=v[b]-ax,uy=v[b+1]-ay,vx=v[c]-ax,vy=v[c+1]-ay;
            float area=ux*vy-uy*vx;
            if (Math.Abs(area)<.00001f) return;
            int left=Math.Max(0,(int)Math.Ceiling(Math.Min(ax,Math.Min(v[b],v[c]))-.5));
            int right=Math.Min(width-1,(int)Math.Floor(Math.Max(ax,Math.Max(v[b],v[c]))-.5));
            int top=Math.Max(0,(int)Math.Ceiling(Math.Min(ay,Math.Min(v[b+1],v[c+1]))-.5));
            int bottom=Math.Min(height-1,(int)Math.Floor(Math.Max(ay,Math.Max(v[b+1],v[c+1]))-.5));
            float ds=vy/area,dt=-uy/area;
            float su=uv[b]-uv[a],tu=uv[c]-uv[a],sv=uv[b+1]-uv[a+1],tv=uv[c+1]-uv[a+1];
            for (int y=top;y<=bottom;y++)
            {
                float s=((left+.5f-ax)*vy-(y+.5f-ay)*vx)/area;
                float t=(ux*(y+.5f-ay)-uy*(left+.5f-ax))/area;
                for (int x=left;x<=right;x++,s+=ds,t+=dt)
                {
                    if(s<0 || t<0 || s+t>1) continue;
                    float fx=Math.Max(0,Math.Min(textureWidth-1,uv[a]+s*su+t*tu-.5f));
                    float fy=Math.Max(0,Math.Min(textureHeight-1,uv[a+1]+s*sv+t*tv-.5f));
                    int ix=(int)fx,iy=(int)fy, p=(iy*textureWidth+ix)*4;
                    int q=ix+1<textureWidth?p+4:p, row=iy+1<textureHeight?textureWidth*4:0;
                    fx-=ix; fy-=iy;
                    float w0=(1-fx)*(1-fy)*opacity,w1=fx*(1-fy)*opacity,w2=(1-fx)*fy*opacity,w3=fx*fy*opacity;
                    int dest=(y*width+x)*4;
                    float alpha=texture[p+3]*w0+texture[q+3]*w1+texture[p+row+3]*w2+texture[q+row+3]*w3;
                    if(alpha<.5f) continue;
                    float remaining=1-alpha/255f;
                    for(int k=0;k<3;k++)
                    {
                        float color=texture[p+k]*w0+texture[q+k]*w1+texture[p+row+k]*w2+texture[q+row+k]*w3;
                        if(dim>0) color*=1-.58f*dim;
                        else if(dim<0) color+=(alpha-color)*(-.42f*dim);
                        Pixels[dest+k]=(byte)Math.Min(255,.5f+color+Pixels[dest+k]*remaining);
                    }
                    Pixels[dest+3]=(byte)Math.Min(255,.5f+alpha+Pixels[dest+3]*remaining);
                }
            }
        }

        public void Dispose()
        {
            if(modelAllocation!=IntPtr.Zero) { Marshal.FreeHGlobal(modelAllocation); modelAllocation=IntPtr.Zero; }
            if(mocAllocation!=IntPtr.Zero) { Marshal.FreeHGlobal(mocAllocation); mocAllocation=IntPtr.Zero; }
            model=IntPtr.Zero;
        }
    }
}
