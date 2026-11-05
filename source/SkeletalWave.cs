using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet {
    // Two-dimensional bone-driven curve skinning. The sleeve is the original rest
    // artwork, not a sequence of full-character pictures or opacity blends.
    internal sealed class SkeletalWave {
        internal const double Duration=2.6;
        private readonly MotionFrame idle,rest,sleeve,body,palm;
        private readonly Point shoulder=new Point(302,133),elbow=new Point(295,188),wrist=new Point(302,235);
        private readonly int width,height;
        private readonly double scale,offsetX,offsetY;
        private MotionFrame cached;private int cachedIndex=-1;
        private readonly byte[] basePixels;
        internal SkeletalWave(MotionFrame target,string directory){
            idle=target;width=target.Width;height=target.Height;
            rest=Read(Path.Combine(directory,"pet.png"));
            var layers=new RestPoseLayers(rest);body=layers.Layers["body"];
            // The old triangular skin fragment is occluded rest-pose paint,
            // not a complete forearm. Do not rotate its dark cut edge into view.
            byte[] arm=new byte[rest.Pixels.Length];foreach(string name in new[]{"upper-arm","forearm"}){byte[] p=layers.Layers[name].Pixels;for(int i=0;i<p.Length;i++)arm[i]|=p[i];}
            // The distal sleeve, cuff, skin and palm now share one atlas mesh.
            // Remove the old cuff so two different cuff edges cannot overlap.
            for(int y=209;y<512;y++)Array.Clear(arm,y*512*4,512*4);
            sleeve=Frame(arm,512,512);
            palm=IsolateWrist(Read(Path.Combine(directory,"wave-keyposes-v2","greeting.png")));
            Int32Rect source=rest.SubjectBounds(),destination=target.SubjectBounds();scale=destination.Height/(double)source.Height;
            offsetX=destination.X+destination.Width*.5-(source.X+source.Width*.5)*scale;offsetY=destination.Y-source.Y*scale;
            basePixels=new byte[width*height*4];PaintStatic(basePixels,body);
        }
        private static MotionFrame Read(string path){var image=new BitmapImage();using(var file=File.OpenRead(path)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=file;image.EndInit();image.Freeze();}return new MotionFrame(image);}
        private static MotionFrame IsolateWrist(MotionFrame source){
            double k=source.Width/1254.0;int left=(int)(573*k),right=(int)(717*k),top=(int)(233*k),bottom=(int)(425*k);
            int seed=(int)(350*k)*source.Width+(int)(655*k);var seen=new bool[source.Width*source.Height];var queue=new int[seen.Length];int first=0,last=1;queue[0]=seed;seen[seed]=true;
            byte[] bytes=new byte[source.Pixels.Length];
            while(first<last){int at=queue[first++],x=at%source.Width,y=at/source.Width;Buffer.BlockCopy(source.Pixels,at*4,bytes,at*4,4);
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<left || xx>=right || yy<top || yy>=bottom)continue;int next=yy*source.Width+xx;
                    if(!seen[next] && source.Pixels[next*4+3]>=16){seen[next]=true;queue[last++]=next;}
                }
            }
            // The pose atlas also contains the adjacent bent upper sleeve.
            // Keep only a short proximal overlap aligned with the cuff's axis;
            // otherwise that neighbouring sleeve becomes a triangular spur.
            for(int y=top;y<(int)(380*k);y++)for(int x=(int)(680*k);x<right;x++){
                Array.Clear(bytes,(y*source.Width+x)*4,4);
            }
            for(int y=(int)(380*k);y<bottom;y++)for(int x=left;x<right;x++){
                double dx=x/k-665,dy=y/k-399;
                if(Math.Abs(dx*.955-dy*.298)>22 || dx*.298+dy*.955>12)Array.Clear(bytes,(y*source.Width+x)*4,4);
            }
            return Frame(bytes,source.Width,source.Height);
        }
        private static MotionFrame Frame(byte[] bytes,int w,int h){var b=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,bytes,w*4);b.Freeze();return new MotionFrame(b);}
        private static double Ease(double x){x=Math.Max(0,Math.Min(1,x));return x*x*x*(x*(x*6-15)+10);}
        private static Point Rotate(Point p,Point origin,double angle,Point destination,double stretch){double c=Math.Cos(angle),s=Math.Sin(angle),x=(p.X-origin.X)*stretch,y=(p.Y-origin.Y)*stretch;return new Point(destination.X+x*c-y*s,destination.Y+x*s+y*c);}
        private Point World(Point p){return new Point(offsetX+p.X*scale,offsetY+p.Y*scale);}
        private Point Skin(Point p,double upper,double lower,Point movingElbow,double foreScale){
            // Smooth, bone-controlled centreline skinning keeps sleeve volume at
            // a deeply bent elbow. Linear matrix blending pinched and inverted
            // the old cuff vertices, making a sharp spur beside the wrist.
            Point movingWrist=Rotate(wrist,elbow,lower,movingElbow,foreScale);
            Point bindControl=new Point(288,192),control=new Point(2*movingElbow.X-(shoulder.X+movingWrist.X)*.5,2*movingElbow.Y-(shoulder.Y+movingWrist.Y)*.5);
            double t=(118-Math.Sqrt(Math.Max(1,13924-64*(p.Y-133))))/32,s=1-t;
            Point bind=new Point(s*s*shoulder.X+2*s*t*bindControl.X+t*t*wrist.X,p.Y);
            Point target=new Point(s*s*shoulder.X+2*s*t*control.X+t*t*movingWrist.X,s*s*shoulder.Y+2*s*t*control.Y+t*t*movingWrist.Y);
            double angle=Math.Atan2(s*(control.Y-shoulder.Y)+t*(movingWrist.Y-control.Y),s*(control.X-shoulder.X)+t*(movingWrist.X-control.X))
                -Math.Atan2(s*(bindControl.Y-shoulder.Y)+t*(wrist.Y-bindControl.Y),s*(bindControl.X-shoulder.X)+t*(wrist.X-bindControl.X));
            Point deformed=Rotate(p,bind,angle,target,1);
            if(p.Y>=170 && p.Y<=204){
                // At a tight bend, a sleeve wider than the centreline radius
                // folds its inner vertices through each other. Compress only
                // that inside surface; leave the outside volume and cuff alone.
                Vector tangent=new Vector(2*(s*(control.X-shoulder.X)+t*(movingWrist.X-control.X)),2*(s*(control.Y-shoulder.Y)+t*(movingWrist.Y-control.Y)));
                Vector acceleration=new Vector(2*(shoulder.X-2*control.X+movingWrist.X),2*(shoulder.Y-2*control.Y+movingWrist.Y));
                double speed=tangent.Length,cross=tangent.X*acceleration.Y-tangent.Y*acceleration.X;
                if(speed>.001 && Math.Abs(cross)>.001){
                    double radius=speed*speed*speed/Math.Abs(cross);Vector inward=new Vector(-tangent.Y/speed,tangent.X/speed)*Math.Sign(cross);
                    Vector delta=deformed-target;double distance=delta.X*inward.X+delta.Y*inward.Y,limit=Math.Max(2,radius*.65);
                    if(distance>limit)deformed-=inward*(distance-limit);
                }
            }
            return World(deformed);
        }
        internal MotionFrame GetAt(double seconds){
            double t=Math.Max(0,Math.Min(Duration,seconds));int index=(int)(t*60);
            // Identity bone transforms use the exact cached bind-pose raster,
            // avoiding a second resample of its antialiased silhouette.
            if(t<=.15 || t>=2.45)return idle;
            if(index==cachedIndex)return cached;
            double u=t<.95?Ease((t-.15)/.8):t<1.65?1:1-Ease((t-1.65)/.8);
            double upper=u*21*Math.PI/180,lower=u*174*Math.PI/180;
            Point e=Rotate(elbow,shoulder,upper,shoulder,1);double length=1-.09*u;
            double wave=t>=.95 && t<=1.65?Math.Sin((t-.95)/.7*Math.PI*2)*8*Math.PI/180:0;
            // Use the raw rest coordinate canvas for body paint, then apply the
            // same fixed outfit normalization as the desktop image.
            byte[] output=(byte[])basePixels.Clone();
            // Local underarm deformer extends the original dark cloth texture.
            // At bind pose it stays inside the original opaque silhouette.
            PaintFill(output,u);
            // The complete palm sits behind the skirt at rest; it emerges by
            // geometry and occlusion, never by changing its opacity.
            var sleeveVertices=new Point[25,11];for(int row=0;row<=24;row++)for(int col=0;col<=10;col++)sleeveVertices[row,col]=Skin(new Point(280+col*4,124+row*4),upper,lower,e,length);
            for(int row=0;row<24;row++)for(int col=0;col<10;col++){
                double x=280+col*4,y=124+row*4;
                Point a=new Point(x,y),b=new Point(x+4,y),c=new Point(x,y+4),d=new Point(x+4,y+4);
                Triangle(output,sleeve,a,b,c,sleeveVertices[row,col],sleeveVertices[row,col+1],sleeveVertices[row+1,col]);
                Triangle(output,sleeve,b,d,c,sleeveVertices[row,col+1],sleeveVertices[row+1,col+1],sleeveVertices[row+1,col]);
            }
            PaintWristMesh(output,Skin(new Point(300,205),upper,lower,e,length),lower,wave,u);
            // Original body excludes the moving arm. It correctly hides the
            // hand/forearm when these pass behind the torso or skirt.
            for(int yy=Math.Max(0,(int)(offsetY+80*scale));yy<Math.Min(height,(int)(offsetY+285*scale));yy++)for(int xx=Math.Max(0,(int)(offsetX+190*scale));xx<Math.Min(width,(int)(offsetX+360*scale));xx++){
                int at=(yy*width+xx)*4;
                if(basePixels[at+3]==0 || (output[at]==basePixels[at] && output[at+1]==basePixels[at+1] && output[at+2]==basePixels[at+2] && output[at+3]==basePixels[at+3]))continue;
                if(basePixels[at+3]==255){Buffer.BlockCopy(basePixels,at,output,at,4);continue;}
                // Reuse the already sampled static foreground instead of four
                // texture fetches for every changed body pixel on every frame.
                double alpha=basePixels[at+3]/255.0,old=output[at+3]/255.0*(1-alpha),total=alpha+old;
                for(int channel=0;channel<3;channel++)output[at+channel]=(byte)Math.Round((basePixels[at+channel]*alpha+output[at+channel]*old)/total);
                output[at+3]=(byte)Math.Round(total*255);
            }
            cached=Frame(output,width,height);cachedIndex=index;return cached;
        }
        private void PaintStatic(byte[] output,MotionFrame source){
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)SampleOver(output,x,y,source,(x+.5-offsetX)/scale-.5,(y+.5-offsetY)/scale-.5);
        }
        private void PaintFill(byte[] output,double amount){
            for(int y=Math.Max(0,(int)(offsetY+124*scale));y<Math.Min(height,(int)(offsetY+239*scale));y++)for(int x=Math.Max(0,(int)(offsetX+282*scale));x<Math.Min(width,(int)(offsetX+318*scale));x++){
                double sx=(x+.5-offsetX)/scale-.5,sy=(y+.5-offsetY)/scale-.5;int ix=(int)Math.Round(sx),iy=(int)Math.Round(sy);
                if(ix>=0 && ix<512 && iy>=0 && iy<512 && (rest.Pixels[(iy*512+ix)*4+3]==255 || amount>0) && sy>=145 && sy<217 && sx>=315-8*amount+(sy-145)*.03)SampleOver(output,x,y,rest,320,sy);
            }
        }
        private Point WristVertex(Point p,Point attachment,double lower,double wave,double amount){
            double k=palm.Width/1254.0,relative=lower-174*Math.PI/180,ratio=.40*scale/k;
            Point root=new Point(665*k,399*k),wristPoint=new Point(655*k,367*k);
            Point v=Rotate(p,root,relative,attachment,ratio),pivot=Rotate(wristPoint,root,relative,attachment,ratio);
            double weight=Ease((399-p.Y/k)/40);
            v=Rotate(v,pivot,wave*weight,pivot,1);
            v.X+=20*(1-amount)*scale*weight;return v;
        }
        private void PaintWristMesh(byte[] output,Point attachment,double lower,double wave,double amount){
            // A continuous piece of the approved art includes the cuff, wrist
            // and fingers. Its triangles share the wrist weights; no skin bridge
            // or independently positioned palm can create a visual joint seam.
            double k=palm.Width/1254.0;
            var vertices=new Point[17,13];for(int row=0;row<=16;row++)for(int col=0;col<=12;col++)vertices[row,col]=WristVertex(new Point((573+col*12)*k,(233+row*12)*k),attachment,lower,wave,amount);
            for(int row=0;row<16;row++)for(int col=0;col<12;col++){
                double x=(573+col*12)*k,y=(233+row*12)*k;
                Point a=new Point(x,y),b=new Point(x+12*k,y),c=new Point(x,y+12*k),d=new Point(x+12*k,y+12*k);
                Point aa=vertices[row,col],bb=vertices[row,col+1],cc=vertices[row+1,col],dd=vertices[row+1,col+1];
                Triangle(output,palm,a,b,c,aa,bb,cc);Triangle(output,palm,b,d,c,bb,dd,cc);
            }
        }
        private void Triangle(byte[] output,MotionFrame texture,Point a,Point b,Point c,Point x,Point y,Point z){
            double det=(y.Y-z.Y)*(x.X-z.X)+(z.X-y.X)*(x.Y-z.Y);if(Math.Abs(det)<.0001)return;
            int left=Math.Max(0,(int)Math.Floor(Math.Min(x.X,Math.Min(y.X,z.X)))),right=Math.Min(width-1,(int)Math.Ceiling(Math.Max(x.X,Math.Max(y.X,z.X))));
            int top=Math.Max(0,(int)Math.Floor(Math.Min(x.Y,Math.Min(y.Y,z.Y)))),bottom=Math.Min(height-1,(int)Math.Ceiling(Math.Max(x.Y,Math.Max(y.Y,z.Y))));
            for(int py=top;py<=bottom;py++)for(int px=left;px<=right;px++){
                double u=((y.Y-z.Y)*(px+.5-z.X)+(z.X-y.X)*(py+.5-z.Y))/det,v=((z.Y-x.Y)*(px+.5-z.X)+(x.X-z.X)*(py+.5-z.Y))/det,w=1-u-v;
                if(u<0 || v<0 || w<0)continue;SampleOver(output,px,py,texture,u*a.X+v*b.X+w*c.X-.5,u*a.Y+v*b.Y+w*c.Y-.5);
            }
        }
        private void SampleOver(byte[] output,int x,int y,MotionFrame texture,double sx,double sy){
            int left=(int)Math.Floor(sx),top=(int)Math.Floor(sy);double fx=sx-left,fy=sy-top,alpha=0,blue=0,green=0,red=0;
            for(int j=0;j<2;j++)for(int i=0;i<2;i++){
                int xx=left+i,yy=top+j;if(xx<0 || yy<0 || xx>=texture.Width || yy>=texture.Height)continue;
                int at=(yy*texture.Width+xx)*4;double weight=(i==0?1-fx:fx)*(j==0?1-fy:fy),a=texture.Pixels[at+3]/255.0*weight;
                alpha+=a;blue+=texture.Pixels[at]*a;green+=texture.Pixels[at+1]*a;red+=texture.Pixels[at+2]*a;
            }
            if(alpha<=0)return;int p=(y*width+x)*4;double old=output[p+3]/255.0*(1-alpha),total=alpha+old;
            output[p]=(byte)Math.Round((blue+output[p]*old)/total);output[p+1]=(byte)Math.Round((green+output[p+1]*old)/total);output[p+2]=(byte)Math.Round((red+output[p+2]*old)/total);output[p+3]=(byte)Math.Round(total*255);
        }
    }
}
