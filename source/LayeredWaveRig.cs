using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace AnnaDesktopPet {
    internal sealed class WaveRigDefinition {
        public double ShoulderX=-.065,ShoulderY=.245;
        public double UpperLength=.13,ForearmLength=.11,HandLength=.07;
        public double UpperWidth=.063,ForearmWidth=.052,HandWidth=.061;
        public double NeutralUpper=8,NeutralElbow=-72,NeutralWrist=0;
        public double RaisedUpper=45,RaisedElbow=135,RaisedWrist=0;
        public double WaveAngle=10,Duration=1.8,Fps=30;
    }
    internal sealed class LayeredWaveRig {
        private readonly MotionFrame idle;
        private readonly WaveRigDefinition rig;
        private readonly BitmapSource body,upper,forearm,hand;
        private readonly double height,cx,top,shoulderX,shoulderY;
        private MotionFrame cached;
        private int cachedIndex=-1;
        internal int FrameCount {get{return (int)Math.Ceiling(rig.Duration*rig.Fps)+1;}}
        internal double Duration {get{return rig.Duration;}}
        internal LayeredWaveRig(MotionFrame original,string folder){
            idle=original;rig=new JavaScriptSerializer().Deserialize<WaveRigDefinition>(File.ReadAllText(Path.Combine(folder,"rig.json")))??new WaveRigDefinition();
            if(rig.Duration<1 || rig.Duration>5 || rig.Fps<20 || rig.Fps>60 || rig.UpperLength<=0 || rig.ForearmLength<=0)throw new InvalidDataException("Invalid wave rig definition");
            Int32Rect bounds=idle.SubjectBounds();height=bounds.Height;cx=bounds.X+bounds.Width*.5;top=bounds.Y;shoulderX=cx+height*rig.ShoulderX;shoulderY=top+height*rig.ShoulderY;
            upper=LoadPart(Path.Combine(folder,"upper-arm.png"));forearm=LoadPart(Path.Combine(folder,"forearm.png"));hand=LoadPart(Path.Combine(folder,"hand.png"));
            body=BuildBody(LoadImage(Path.Combine(folder,"body-fill.png")));
        }
        private static BitmapSource LoadImage(string path){var bitmap=new BitmapImage();using(var stream=File.OpenRead(path)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();}return bitmap;}
        private static BitmapSource LoadPart(string path){var image=LoadImage(path);var bounds=new MotionFrame(image).SubjectBounds();var crop=new CroppedBitmap(image,bounds);crop.Freeze();return crop;}
        private BitmapSource BuildBody(BitmapSource fill){
            var source=new MotionFrame(fill);Int32Rect bounds=source.SubjectBounds(),target=idle.SubjectBounds();double scale=height/bounds.Height;
            var v=new DrawingVisual();using(var dc=v.RenderOpen())dc.DrawImage(fill,new Rect(cx-(bounds.X+bounds.Width*.5)*scale,target.Y+target.Height-(bounds.Y+bounds.Height)*scale,source.Width*scale,source.Height*scale));
            var render=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);render.Render(v);render.Freeze();var replacement=new MotionFrame(render);
            var region=new StreamGeometry();using(var g=region.Open()){
                g.BeginFigure(new Point(cx-height*.14,top+height*.235),true,true);
                g.LineTo(new Point(cx-height*.015,top+height*.235),true,false);
                g.LineTo(new Point(cx-height*.015,top+height*.465),true,false);
                g.LineTo(new Point(cx-height*.14,top+height*.465),true,false);
            }region.Freeze();
            byte[] pixels=(byte[])idle.Pixels.Clone();
            Rect box=region.Bounds;for(int y=Math.Max(0,(int)box.Top);y<Math.Min(idle.Height,(int)Math.Ceiling(box.Bottom));y++)for(int x=Math.Max(0,(int)box.Left);x<Math.Min(idle.Width,(int)Math.Ceiling(box.Right));x++)if(region.FillContains(new Point(x+.5,y+.5))){int at=(y*idle.Width+x)*4;for(int channel=0;channel<4;channel++)pixels[at+channel]=replacement.Pixels[at+channel];}
            var result=BitmapSource.Create(idle.Width,idle.Height,96,96,PixelFormats.Bgra32,null,pixels,idle.Width*4);result.Freeze();return result;
        }
        private static double Ease(double x){x=Math.Max(0,Math.Min(1,x));return x*x*x*(x*(x*6-15)+10);}
        private static double Mix(double a,double b,double x){return a+(b-a)*x;}
        internal MotionFrame GetAt(double seconds){
            if(seconds<=.1 || seconds>=rig.Duration)return idle;
            int index=(int)(seconds*rig.Fps);if(index==cachedIndex)return cached;
            double t=index/rig.Fps,u;
            if(t<.65)u=Ease((t-.10)/.55);else if(t<=1.15)u=1;else u=1-Ease((t-1.15)/.55);
            double upperAngle=Mix(rig.NeutralUpper,rig.RaisedUpper,u),elbowAngle=Mix(rig.NeutralElbow,rig.RaisedElbow,u),wristAngle=Mix(rig.NeutralWrist,rig.RaisedWrist,u);
            if(t>=.65 && t<=1.15)wristAngle+=Math.Sin((t-.65)/.5*Math.PI*2)*rig.WaveAngle;
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
                // The torso always occludes a forearm returning behind the back.
                // Keep the upper sleeve in front at every angle; never switch the whole arm's depth.
                DrawArm(dc,upperAngle,elbowAngle,wristAngle);
                dc.DrawImage(body,new Rect(0,0,idle.Width,idle.Height));
                dc.PushTransform(new TranslateTransform(shoulderX,shoulderY));
                dc.PushTransform(new RotateTransform(upperAngle));
                dc.DrawImage(upper,new Rect(-height*rig.UpperWidth*.5,-height*rig.UpperLength/.88*.06,height*rig.UpperWidth,height*rig.UpperLength/.88));
                dc.Pop();dc.Pop();
            }
            var output=new RenderTargetBitmap(idle.Width,idle.Height,96,96,PixelFormats.Pbgra32);output.Render(visual);output.Freeze();cached=new MotionFrame(output);cachedIndex=index;return cached;
        }
        private void DrawArm(DrawingContext dc,double upperAngle,double elbowAngle,double wristAngle){
            double a=height*rig.UpperLength,b=height*rig.ForearmLength,c=height*rig.HandLength;
            dc.PushTransform(new TranslateTransform(shoulderX,shoulderY));dc.PushTransform(new RotateTransform(upperAngle));
            dc.DrawImage(upper,new Rect(-height*rig.UpperWidth*.5,-a/.88*.06,height*rig.UpperWidth,a/.88));
            dc.PushTransform(new TranslateTransform(0,a));dc.PushTransform(new RotateTransform(elbowAngle));
            dc.DrawImage(forearm,new Rect(-height*rig.ForearmWidth*.5,-b/.88*.06,height*rig.ForearmWidth,b/.88));
            dc.PushTransform(new TranslateTransform(0,b));dc.PushTransform(new RotateTransform(wristAngle));
            dc.DrawImage(hand,new Rect(-height*rig.HandWidth*.5,-c/.82*.10,height*rig.HandWidth,c/.82));
            for(int i=0;i<6;i++)dc.Pop();
        }
    }
}
