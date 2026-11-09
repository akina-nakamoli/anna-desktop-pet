using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed class MotionFrame
    {
        public readonly BitmapSource Image;
        public readonly byte[] Pixels;
        public int Width { get { return Image.PixelWidth; } }
        public int Height { get { return Image.PixelHeight; } }
        public MotionFrame(BitmapSource source)
        {
            Image = source.Format==PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); Image.Freeze();
            Pixels = new byte[Width * Height * 4]; Image.CopyPixels(Pixels, Width * 4, 0);
        }
        public Int32Rect SubjectBounds()
        {
            int left = Width, top = Height, right = -1, bottom = -1;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (Pixels[(y * Width + x) * 4 + 3] >= 32)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (right < left) throw new InvalidDataException("角色姿态没有可见像素。");
            return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        }
    }

    internal sealed class MotionController
    {
        internal static readonly string[] PoseNames = new string[] { "wave", "happy", "sleep", "think", "eat", "speak" };
        private readonly Dictionary<string, MotionFrame> frames = new Dictionary<string, MotionFrame>();
        private MotionFrame[] waveFrames, blinkFrames, speechFrames, happyFrames, walkFrames;
        private MotionFrame[] sittingFrames;
        private bool waveTrial;
        private MotionFrame[] anchoredWave;
        private SkeletalWave skeletalWave;
        private PngSequence pngWave;
        public bool HasWaveTrial {get{return waveTrial;}}
        public bool HasLifeAnimation {get{return sittingFrames!=null;}}
        public int WaveFrameCount {get{return pngWave!=null?pngWave.Count:skeletalWave!=null?157:waveFrames==null?0:waveFrames.Length;}}
        public int SpeechFrameCount {get{return speechFrames==null?0:speechFrames.Length;}}
        public int SitFrameCount {get{return sittingFrames==null?0:sittingFrames.Length;}}
        private static readonly int[] MouthCycle = { 0, 1, 2, 3, 1, 2 };
        private static readonly int[] WaveCycle = { 0, 1, 2, 3, 3, 2, 1, 0 };
        private static readonly int[] HappyCycle = { 0, 1, 2, 2, 1, 0 };
        private static readonly int[] Walk8Cycle = { 0, 1, 2, 6, 7, 5, 4, 3 };
        public bool HasPoses { get; private set; }
        public bool HasWaveAnimation { get { return waveFrames != null; } }
        public bool HasFaceAnimation { get { return blinkFrames != null || speechFrames != null; } }
        public bool HasBlinkAnimation { get { return blinkFrames != null; } }
        public bool HasSpeechAnimation { get { return speechFrames != null; } }
        public bool HasWalkAnimation { get { return walkFrames != null; } }
        public int WalkFrameCount { get { return walkFrames == null ? 0 : walkFrames.Length; } }
        public double WalkFramesPerSecond { get { return HasLifeAnimation?12.0:WalkFrameCount == 8 ? 10.0 : 6.0; } }
        public bool HasHappyAnimation { get { return happyFrames != null; } }
        public string Status { get; private set; }
        public MotionController(BitmapSource idle)
        {
            MotionFrame baseFrame = new MotionFrame(idle); frames["idle"] = baseFrame;
            foreach (string name in PoseNames) frames[name] = baseFrame;
            Status = "使用待机形象与动态效果";
            string path = Appearance.Asset("poses.png");
            try
            {
                BitmapImage sheet = new BitmapImage();
                using (Stream stream = File.Exists(path) ? File.OpenRead(path) : Assembly.GetExecutingAssembly().GetManifestResourceStream("AnnaDesktopPet.poses.png"))
                {
                    if (stream == null) throw new FileNotFoundException("动作姿态图不可用。");
                    sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.StreamSource = stream; sheet.EndInit(); sheet.Freeze();
                }
                const int columns = 3, rows = 2;
                if (sheet.PixelWidth < columns || sheet.PixelHeight < rows) throw new InvalidDataException("动作图太小。");
                Int32Rect idleBounds = baseFrame.SubjectBounds();
                for (int i = 0; i < PoseNames.Length; i++)
                {
                    int sourceIndex = Appearance.Outfit=="默认" || Appearance.Outfit=="校服1" ? i : new int[]{1,2,5,4,3,0}[i];
                    int x0 = (sourceIndex % columns) * sheet.PixelWidth / columns, x1 = ((sourceIndex % columns) + 1) * sheet.PixelWidth / columns;
                    int y0 = (sourceIndex / columns) * sheet.PixelHeight / rows, y1 = ((sourceIndex / columns) + 1) * sheet.PixelHeight / rows;
                    CroppedBitmap crop = new CroppedBitmap(sheet, new Int32Rect(x0, y0, x1 - x0, y1 - y0)); crop.Freeze();
                    MotionFrame raw = new MotionFrame(crop);
                    frames[PoseNames[i]] = Normalize(raw, baseFrame, idleBounds);
                }
                HasPoses = true; Status = "6 种人物姿态已就绪";
            }
            catch (Exception ex)
            {
                foreach (string name in PoseNames) frames[name] = baseFrame;
                Program.Log("Motion image fallback: " + ex.Message);
            }
            waveFrames = LoadSequence("animation-wave.png", "AnnaDesktopPet.animation-wave.png", baseFrame);
            blinkFrames = LoadSequence("animation-blink.png", "AnnaDesktopPet.animation-blink.png", baseFrame);
            speechFrames = LoadSequence("animation-speak.png", "AnnaDesktopPet.animation-speak.png", baseFrame);
            happyFrames = LoadSequence("animation-happy.png", "AnnaDesktopPet.animation-happy.png", baseFrame);
            // Walking is retired. Do not load its assets merely because an old pack has them.
            walkFrames = null;
            if (waveFrames != null || blinkFrames != null || speechFrames != null) Status += "；连续动作已就绪";
            LoadOutfitPack(baseFrame);
        }
        private void LoadOutfitPack(MotionFrame idle,string explicitPath=null){
            string path=explicitPath ?? Appearance.Asset("animation-life.png");if(!File.Exists(path))return;
            try{
                // HD speaking must not depend on a retired walking sheet. The legacy
                // atlas still uses rows 0/1/2/3 for wave/walk/speak/sit respectively.
                string directory=Path.GetDirectoryName(path);string[] actions={"wave","speak","sit"};int[] legacyRows={0,2,3};bool hd=true,mid=true;foreach(string action in actions){if(!File.Exists(Path.Combine(directory,"animation-"+action+"-hd.png")))hd=false;if(!File.Exists(Path.Combine(directory,"animation-"+action+"-mid.png")))mid=false;}
                int count=hd&&mid?8:4;var raw=new MotionFrame[count*actions.Length];
                if(hd){
                    for(int row=0;row<actions.Length;row++){
                        MotionFrame[] keys=ReadHdSheet(Path.Combine(directory,"animation-"+actions[row]+"-hd.png"));
                        MotionFrame[] between=count==8?ReadHdSheet(Path.Combine(directory,"animation-"+actions[row]+"-mid.png")):null;
                        // One school-uniform wave key mistakenly changes arm; use the matching raised-arm key.
                        if(row==0 && Path.GetFileName(directory)=="校服2")keys[2]=keys[1];
                        for(int col=0;col<4;col++){raw[row*count+(count==8?col*2:col)]=keys[col];if(count==8)raw[row*count+col*2+1]=between[col];}
                    }
                }else{
                    var image=new BitmapImage();using(var stream=File.OpenRead(path)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();}
                    for(int i=0;i<raw.Length;i++){int col=i%4,row=legacyRows[i/4];int x=col*image.PixelWidth/4,y=row*image.PixelHeight/5;raw[i]=IsolateSprite(new CroppedBitmap(image,new Int32Rect(x,y,(col+1)*image.PixelWidth/4-x,(row+1)*image.PixelHeight/5-y)));}
                }
                Int32Rect reference=idle.SubjectBounds();double standingHeight=0;for(int i=0;i<count*2;i++)standingHeight=Math.Max(standingHeight,raw[i].SubjectBounds().Height);
                double scale=reference.Height/standingHeight;double center=reference.X+reference.Width*.5,foot=reference.Y+reference.Height;
                var aligned=new MotionFrame[raw.Length];
                for(int i=0;i<raw.Length;i++){
                    Int32Rect bounds=raw[i].SubjectBounds();double anchor=LowerBodyCenterX(raw[i]);
                    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(raw[i].Image,new Rect(center-anchor*scale,foot-(bounds.Y+bounds.Height)*scale,raw[i].Width*scale,raw[i].Height*scale));
                    var rendered=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);rendered.Render(visual);rendered.Freeze();aligned[i]=new MotionFrame(rendered);
                }
                waveFrames=new MotionFrame[count];walkFrames=null;speechFrames=new MotionFrame[count];sittingFrames=new MotionFrame[count];
                Array.Copy(aligned,0,waveFrames,0,count);Array.Copy(aligned,count,speechFrames,0,count);Array.Copy(aligned,count*2,sittingFrames,0,count);
                // Keep the original clean standing art; animation sheets must not replace it.
                frames["sit"]=sittingFrames[count-1];Status=(hd?"高清":"")+count+"帧服装说话动画已就绪";
                LoadWaveTrial(idle,directory);
            }catch(Exception ex){Program.Log("Outfit animation pack failed: "+ex.Message);}
        }
        private void LoadWaveTrial(MotionFrame idle,string directory){
            if(Path.GetFileName(directory)!="校服2")return;
            string pngDirectory=Path.Combine(directory,"png-actions","wave");
            string drawnDirectory=Path.Combine(directory,"png-actions","wave-newdrawn-v1");
            if(File.Exists(Path.Combine(drawnDirectory,"animation.json")))pngDirectory=drawnDirectory;
            string denseDirectory=Path.Combine(directory,"png-actions","wave-newdrawn-v2");
            if(File.Exists(Path.Combine(denseDirectory,"animation.json")))pngDirectory=denseDirectory;
            string transitionDirectory=Path.Combine(directory,"png-actions","wave-newdrawn-v3");
            if(File.Exists(Path.Combine(transitionDirectory,"animation.json")))pngDirectory=transitionDirectory;
            if(File.Exists(Path.Combine(pngDirectory,"animation.json"))){
                pngWave=new PngSequence(pngDirectory,idle);skeletalWave=null;waveTrial=true;
                Status+="；校服2 PNG逐帧"+(pngWave.Source=="legacy-render-test"?"（旧动作测试素材）":pngWave.Source=="new-drawn-transition-draft"?"（伸收修正试作）":pngWave.Source=="new-drawn-dense-draft"?"（补帧试作）":pngWave.Source=="new-drawn-draft"?"（新绘试作）":"");return;
            }
            if(File.Exists(Path.Combine(directory,"wave-keyposes-v2","greeting.png"))){
                skeletalWave=new SkeletalWave(idle,directory);waveTrial=true;Status+="；校服2肩肘腕骨骼蒙皮挥手";return;
            }
            string keyDirectory=Path.Combine(directory,"wave-keyposes-v2");
            if(File.Exists(Path.Combine(keyDirectory,"middle.png")) && File.Exists(Path.Combine(keyDirectory,"greeting.png"))){
                LoadKeyPoseTrial(idle,keyDirectory);return;
            }
            string sheet=Path.Combine(directory,"animation-wave-hd.png");if(!File.Exists(sheet))return;
            BuildAnchoredWave(idle,AlignWave(ReadHdSheet(sheet)[1],idle),Path.Combine(directory,"wave-body-fill.png"));
            waveFrames=anchoredWave;waveTrial=true;Status+="；校服2固定身体、局部手臂单次挥动";
        }
        private void LoadKeyPoseTrial(MotionFrame idle,string directory){
            var poses=new MotionFrame[3];poses[0]=idle;
            string[] names={"middle.png","greeting.png"};
            for(int i=0;i<2;i++){
                var image=new BitmapImage();using(var stream=File.OpenRead(Path.Combine(directory,names[i]))){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();}
                poses[i+1]=AlignWave(new MotionFrame(image),idle);
            }
            // Pose-study playback, not a skeletal animation. Short transitions
            // avoid abrupt image replacement while keeping each key pose readable.
            anchoredWave=new MotionFrame[79];
            for(int i=0;i<anchoredWave.Length;i++){
                double t=i/30.0;int from,to;double blend;
                if(t<=.15 || t>=2.45){anchoredWave[i]=idle;continue;}
                if(t<.5){from=0;to=1;blend=(t-.15)/.35;}
                else if(t<.85){from=1;to=1;blend=0;}
                else if(t<1.2){from=1;to=2;blend=(t-.85)/.35;}
                else if(t<1.65){from=2;to=2;blend=0;}
                else if(t<2.0){from=2;to=1;blend=(t-1.65)/.35;}
                else if(t<2.1){from=1;to=1;blend=0;}
                else{from=1;to=0;blend=(t-2.1)/.35;}
                if(from==to){anchoredWave[i]=poses[from];continue;}
                blend=Math.Max(0,Math.Min(1,blend));blend=blend*blend*(3-2*blend);
                // Mix premultiplied pixels directly; source-over would dim or
                // punch holes through the unchanged opaque parts of the body.
                var pixels=new byte[idle.Width*idle.Height*4];
                byte[] a=poses[from].Pixels,b=poses[to].Pixels;
                for(int p=0;p<pixels.Length;p+=4){double alpha=a[p+3]*(1-blend)+b[p+3]*blend;pixels[p+3]=(byte)Math.Round(alpha);if(alpha>0)for(int c=0;c<3;c++)pixels[p+c]=(byte)Math.Round((a[p+c]*a[p+3]*(1-blend)+b[p+c]*b[p+3]*blend)/alpha);}
                var output=BitmapSource.Create(idle.Width,idle.Height,96,96,PixelFormats.Bgra32,null,pixels,idle.Width*4);output.Freeze();anchoredWave[i]=new MotionFrame(output);
            }
            waveFrames=anchoredWave;waveTrial=true;Status+="；校服2三姿势挥手试用";
        }
        private void BuildAnchoredWave(MotionFrame idle,MotionFrame raised,string fillPath){
            var bounds=idle.SubjectBounds();double h=bounds.Height,cx=bounds.X+bounds.Width*.5,top=bounds.Y;
            MotionFrame body=BuildWaveBody(idle,fillPath);
            double pivotX=cx-h*.075,pivotY=top+h*.25;
            var arm=new StreamGeometry();using(var g=arm.Open()){
                g.BeginFigure(new Point(cx-h*.30,top+h*.15),true,true);
                g.LineTo(new Point(cx-h*.13,top+h*.15),true,false);
                g.LineTo(new Point(cx-h*.13,top+h*.22),true,false);
                g.LineTo(new Point(cx-h*.035,top+h*.265),true,false);
                g.LineTo(new Point(cx-h*.045,top+h*.38),true,false);
                g.LineTo(new Point(cx-h*.145,top+h*.46),true,false);
                g.LineTo(new Point(cx-h*.30,top+h*.34),true,false);
            }arm.Freeze();anchoredWave=new MotionFrame[43];
            for(int i=0;i<anchoredWave.Length;i++){
                double t=i/30.0;if(t<=.10 || t>=1.40){anchoredWave[i]=idle;continue;}
                double angle,hiddenOffset;
                if(t<.45){double u=(t-.10)/.35;u=u*u*(3-2*u);angle=-62*(1-u);hiddenOffset=h*.13*(1-u);}
                else if(t<1.0){double u=(t-.45)/.55;angle=Math.Sin(u*Math.PI*2)*4;hiddenOffset=0;}
                else{double u=(t-1.0)/.40;u=u*u*(3-2*u);angle=-62*u;hiddenOffset=h*.13*u;}
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                    // The static body has no old arm. Never paint a complete idle sprite
                    // over the moving arm: that restores the old sleeve/hand on top.
                    dc.PushClip(new RectangleGeometry(new Rect(0,0,cx-h*.01,idle.Height)));
                    dc.PushTransform(new TranslateTransform(hiddenOffset,0));dc.PushTransform(new RotateTransform(angle,pivotX,pivotY));dc.PushClip(arm);dc.DrawImage(raised.Image,new Rect(0,0,idle.Width,idle.Height));dc.Pop();dc.Pop();dc.Pop();dc.Pop();
                    dc.DrawImage(body.Image,new Rect(0,0,idle.Width,idle.Height));
                }
                var image=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();anchoredWave[i]=new MotionFrame(image);
            }
        }
        private static MotionFrame BuildWaveBody(MotionFrame idle,string path){
            if(!File.Exists(path))throw new FileNotFoundException("Missing wave body occlusion fill",path);
            var fill=new BitmapImage();using(var stream=File.OpenRead(path)){fill.BeginInit();fill.CacheOption=BitmapCacheOption.OnLoad;fill.StreamSource=stream;fill.EndInit();fill.Freeze();}
            var source=new MotionFrame(fill);Int32Rect src=source.SubjectBounds(),dst=idle.SubjectBounds();
            double h=dst.Height,cx=dst.X+dst.Width*.5,top=dst.Y,scale=h/src.Height;
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(fill,new Rect(cx-(src.X+src.Width*.5)*scale,top+h-(src.Y+src.Height)*scale,source.Width*scale,source.Height*scale));
            var render=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);render.Render(visual);render.Freeze();var replacement=new MotionFrame(render);
            byte[] pixels=(byte[])idle.Pixels.Clone();
            for(int y=Math.Max(0,(int)(top+h*.235));y<Math.Min(idle.Height,(int)(top+h*.465));y++)for(int x=Math.Max(0,(int)(cx-h*.14));x<Math.Min(idle.Width,(int)(cx-h*.015));x++){
                int at=(y*idle.Width+x)*4;Buffer.BlockCopy(replacement.Pixels,at,pixels,at,4);
            }
            var image=BitmapSource.Create(idle.Width,idle.Height,96,96,PixelFormats.Bgra32,null,pixels,idle.Width*4);image.Freeze();return new MotionFrame(image);
        }
        private static MotionFrame AlignWave(MotionFrame source,MotionFrame idle){
            Int32Rect target=idle.SubjectBounds(),bounds=source.SubjectBounds();double scale=target.Height/(double)bounds.Height,center=target.X+target.Width*.5,foot=target.Y+target.Height;
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(source.Image,new Rect(center-LowerBodyCenterX(source)*scale,foot-(bounds.Y+bounds.Height)*scale,source.Width*scale,source.Height*scale));
            var image=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return new MotionFrame(image);
        }
        private static MotionFrame[] ReadHdSheet(string path){
            var image=new BitmapImage();using(var stream=File.OpenRead(path)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();}
            if(image.PixelWidth<1000 || image.PixelHeight<1000)throw new InvalidDataException("高清动作图分辨率不足："+Path.GetFileName(path));var result=new MotionFrame[4];
            for(int i=0;i<4;i++){int col=i%2,row=i/2,x=col*image.PixelWidth/2,y=row*image.PixelHeight/2;result[i]=IsolateSprite(new CroppedBitmap(image,new Int32Rect(x,y,(col+1)*image.PixelWidth/2-x,(row+1)*image.PixelHeight/2-y)));}return result;
        }
        // Ignore detached fragments from neighbouring cells; keep the two head-height ghost flames.
        private static MotionFrame IsolateSprite(BitmapSource source){
            var frame=new MotionFrame(source);int w=frame.Width,h=frame.Height;var labels=new int[w*h];var sizes=new List<int>{0};var tops=new List<int>{h};var bottoms=new List<int>{0};var queue=new int[w*h];int label=0;
            for(int p=0;p<labels.Length;p++)if(labels[p]==0 && frame.Pixels[p*4+3]>=32){label++;int count=0,top=h,bottom=0,read=0,write=1;queue[0]=p;labels[p]=label;while(read<write){int n=queue[read++],x=n%w,y=n/w;count++;top=Math.Min(top,y);bottom=Math.Max(bottom,y);for(int direction=0;direction<4;direction++){int next=direction==0?(x>0?n-1:-1):direction==1?(x+1<w?n+1:-1):direction==2?(y>0?n-w:-1):(y+1<h?n+w:-1);if(next>=0 && labels[next]==0 && frame.Pixels[next*4+3]>=32){labels[next]=label;queue[write++]=next;}}}sizes.Add(count);tops.Add(top);bottoms.Add(bottom);}
            int main=0;for(int i=1;i<sizes.Count;i++)if(sizes[i]>sizes[main])main=i;if(main==0)return frame;
            var keep=new bool[w*h];for(int p=0;p<labels.Length;p++){int id=labels[p];if(id==main || id>0 && sizes[id]>=24 && bottoms[id]<tops[main]+(bottoms[main]-tops[main])*.28)keep[p]=true;}
            var expanded=(bool[])keep.Clone();for(int p=0;p<keep.Length;p++)if(keep[p]){int x=p%w,y=p/w;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)if(x+dx>=0 && x+dx<w && y+dy>=0 && y+dy<h)expanded[(y+dy)*w+x+dx]=true;}
            for(int p=0;p<expanded.Length;p++)if(!expanded[p])frame.Pixels[p*4+3]=0;
            var clean=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,frame.Pixels,w*4);clean.Freeze();return new MotionFrame(clean);
        }
        private static MotionFrame[] LoadSequence(string name, string resource, MotionFrame idle, int columns = 4, int rows = 1)
        {
            if(Appearance.Outfit!="默认" && Appearance.Outfit!="校服1" && !File.Exists(Appearance.Asset(name)))return null;
            try
            {
                string path = Appearance.Asset(name);
                BitmapImage sheet = new BitmapImage();
                using (Stream stream = File.Exists(path) ? File.OpenRead(path) : Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.StreamSource = stream; sheet.EndInit(); sheet.Freeze();
                }
                if (sheet.PixelWidth < columns || sheet.PixelHeight < rows || sheet.PixelWidth % columns != 0 || sheet.PixelHeight % rows != 0) return null;
                if (name == "animation-walk8.png" && (sheet.PixelWidth != 1536 || sheet.PixelHeight != 1024 || columns != 4 || rows != 2)) throw new InvalidDataException("8-frame walking sheet size mismatch.");
                MotionFrame[] raw = new MotionFrame[columns * rows]; Int32Rect reference = idle.SubjectBounds();
                double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
                double[] shiftX = new double[raw.Length], shiftY = new double[raw.Length];
                double referenceCenter = 0, referenceFoot = 0;
                for (int i = 0; i < raw.Length; i++)
                {
                    int x0 = (i % columns) * sheet.PixelWidth / columns, x1 = ((i % columns) + 1) * sheet.PixelWidth / columns;
                    int y0 = name == "animation-walk8.png" ? (i < columns ? 0 : 523) : (i / columns) * sheet.PixelHeight / rows;
                    int y1 = name == "animation-walk8.png" ? (i < columns ? 523 : sheet.PixelHeight) : ((i / columns) + 1) * sheet.PixelHeight / rows;
                    CroppedBitmap crop = new CroppedBitmap(sheet, new Int32Rect(x0, y0, x1 - x0, y1 - y0)); crop.Freeze();
                    raw[i] = new MotionFrame(crop); Int32Rect bounds = raw[i].SubjectBounds();
                    // Wave drawings can shift horizontally between cells. Anchor the lower body and feet,
                    // never the hand, before computing the shared scale and canvas.
                    {
                        double centerX = (name == "animation-walk.png" || name == "animation-walk8.png") ? UpperHeadCenterX(raw[i]) : LowerBodyCenterX(raw[i]);
                        double frameFoot = (name == "animation-walk.png" || name == "animation-walk8.png") ? bounds.Y : bounds.Y + bounds.Height;
                        if (i == 0) { referenceCenter = centerX; referenceFoot = frameFoot; }
                        shiftX[i] = referenceCenter - centerX; shiftY[i] = referenceFoot - frameFoot;
                    }
                    left = Math.Min(left, bounds.X + shiftX[i]); top = Math.Min(top, bounds.Y + shiftY[i]);
                    right = Math.Max(right, bounds.X + bounds.Width + shiftX[i]); bottom = Math.Max(bottom, bounds.Y + bounds.Height + shiftY[i]);
                }
                // Use one common transform for every frame: varying hand extents must not resize the entire character.
                double availableHeight = name == "animation-walk8.png" ? Math.Max(1, reference.Height - 24.0) : reference.Height;
                double scale = Math.Min(availableHeight / (bottom - top), (idle.Width - 32.0) / (right - left));
                double width = (right - left) * scale, height = (bottom - top) * scale;
                double center = reference.X + reference.Width / 2.0;
                double x = Math.Max(16, Math.Min(idle.Width - width - 16, center - width / 2));
                double foot = Math.Min(idle.Height - 12, reference.Y + reference.Height);
                double y = Math.Max(0, foot - height);
                MotionFrame[] result = new MotionFrame[raw.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    DrawingVisual visual = new DrawingVisual();
                    using (DrawingContext dc = visual.RenderOpen()) {
                        // Preserve the configured height while matching the happy pose head and torso to idle.
                        double aspectX = (name == "animation-happy.png" || (name == "animation-walk.png" || name == "animation-walk8.png")) ? .91 : 1.0;
                        double originX = x + (shiftX[i] - left) * scale;
                        double adjustedX = center + (originX - center) * aspectX;
                        dc.PushTransform(new ScaleTransform(.5, .5));
                        dc.DrawImage(raw[i].Image, new Rect(adjustedX, y + (shiftY[i] - top) * scale, raw[i].Width * scale * aspectX, raw[i].Height * scale)); dc.Pop();
                    }
                    RenderTargetBitmap aligned = new RenderTargetBitmap(idle.Width / 2, idle.Height / 2, 96, 96, PixelFormats.Pbgra32); aligned.Render(visual); aligned.Freeze();
                    result[i] = new MotionFrame(aligned);
                }
                return result;
            }
            catch (Exception ex) { Program.Log("Animation fallback " + name + ": " + ex.Message); return null; }
        }
        internal static double UpperHeadCenterX(MotionFrame frame)
        {
            double sum = 0, weight = 0;
            for (int y = frame.Height * 4 / 100; y < frame.Height * 16 / 100; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    byte alpha = frame.Pixels[(y * frame.Width + x) * 4 + 3];
                    if (alpha >= 32) { sum += x * (double)alpha; weight += alpha; }
                }
            if (weight == 0) throw new InvalidDataException("动作帧缺少头部。");
            return sum / weight;
        }
        internal static double LowerBodyCenterX(MotionFrame frame)
        {
            double sum = 0, weight = 0;
            for (int y = frame.Height * 3 / 5; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    byte alpha = frame.Pixels[(y * frame.Width + x) * 4 + 3];
                    if (alpha >= 32) { sum += x * (double)alpha; weight += alpha; }
                }
            if (weight == 0) throw new InvalidDataException("动作帧缺少下半身。");
            return sum / weight;
        }
        internal static MotionFrame Normalize(MotionFrame source, MotionFrame idle, Int32Rect idleBounds)
        {
            Int32Rect bounds = source.SubjectBounds();
            CroppedBitmap body = new CroppedBitmap(source.Image, bounds); body.Freeze();
            double maxWidth = idle.Width - 32;
            double scale = Math.Min((double)idleBounds.Height / bounds.Height, maxWidth / bounds.Width);
            double width = bounds.Width * scale, height = bounds.Height * scale;
            double center = idleBounds.X + idleBounds.Width / 2.0;
            double x = Math.Max(16, Math.Min(idle.Width - width - 16, center - width / 2));
            double bottom = Math.Min(idle.Height - 12, idleBounds.Y + idleBounds.Height);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) dc.DrawImage(body, new Rect(x, Math.Max(0, bottom - height), width, height));
            RenderTargetBitmap aligned = new RenderTargetBitmap(idle.Width, idle.Height, 96, 96, PixelFormats.Pbgra32); aligned.Render(visual); aligned.Freeze();
            return new MotionFrame(aligned);
        }
        public MotionFrame Get(string name) { if(name=="wave" && pngWave!=null)return frames["idle"];MotionFrame frame; return frames.TryGetValue(name ?? "idle", out frame) ? frame : frames["idle"]; }
        public MotionFrame GetAt(string name, double seconds)
        {
            if(name=="wave" && pngWave!=null)return pngWave.GetAt(seconds);
            if(name=="wave" && skeletalWave!=null)return skeletalWave.GetAt(seconds);
            if(name=="wave" && waveTrial){
                double t=Math.Max(0,seconds);
                if(t>=(anchoredWave.Length-1)/30.0)return frames["idle"];return anchoredWave[Math.Min(anchoredWave.Length-1,(int)(t*30))];
            }
            if(name=="sit" && sittingFrames!=null){int last=sittingFrames.Length-1;int index=seconds<1.2?Math.Min(last,(int)(Math.Max(0,seconds)*sittingFrames.Length/1.2)):seconds<15?last:Math.Max(0,last-(int)((seconds-15)*sittingFrames.Length/1.2));return sittingFrames[index];}
            if (name == "walk" && walkFrames != null) { int index = (int)(Math.Max(0, seconds) * WalkFramesPerSecond) % walkFrames.Length; return walkFrames[!HasLifeAnimation && walkFrames.Length == 8 ? Walk8Cycle[index] : index]; }
            if (name == "wave" && waveFrames != null)
            {
                if(waveFrames.Length==8)return waveFrames[(int)(Math.Max(0,seconds)*12)%8];
                return waveFrames[WaveCycle[(int)(Math.Max(0, seconds) * 9) % WaveCycle.Length]];
            }
            if (name == "happy" && happyFrames != null)
                return happyFrames[HappyCycle[(int)(Math.Max(0, seconds) * 5) % HappyCycle.Length]];
            if (name == "idle" && blinkFrames != null)
            {
                double phase = Math.Max(0, seconds) % 5.7;
                int index = phase < .08 ? 1 : phase < .19 ? 2 : phase < .27 ? 1 : 0;
                return blinkFrames[index];
            }
            if (name == "speak" && speechFrames != null)
            {
                if(speechFrames.Length==8)return speechFrames[(int)(Math.Max(0,seconds)*12)%8];
                return speechFrames[MouthCycle[(int)(Math.Max(0, seconds) * 6) % MouthCycle.Length]];
            }
            return Get(name);
        }
    }
}
