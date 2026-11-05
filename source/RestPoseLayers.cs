using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet {
    // Visible rest-pose layers only. Hidden joint/torso paint is deliberately not
    // inferred here: it requires a separate art pass before these layers can move.
    internal sealed class RestPoseLayers {
        internal readonly Dictionary<string, MotionFrame> Layers = new Dictionary<string, MotionFrame>();
        private readonly int width,height;
        private RestPoseLayers(int w,int h){width=w;height=h;}
        internal static RestPoseLayers Load(string directory,MotionFrame reference){
            var model=new RestPoseLayers(reference.Width,reference.Height);
            foreach(string name in new[]{"body","upper-arm","forearm","hand"}){
                var image=new BitmapImage();using(var file=File.OpenRead(Path.Combine(directory,name+".png"))){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=file;image.EndInit();image.Freeze();}
                if(image.PixelWidth!=reference.Width || image.PixelHeight!=reference.Height)throw new InvalidDataException("Rest layer canvas mismatch: "+name);
                model.Layers[name]=new MotionFrame(image);
            }
            byte[] merged=model.Compose().Pixels;
            for(int i=0;i<merged.Length;i++)if(merged[i]!=reference.Pixels[i])throw new InvalidDataException("Rest layers do not match outfit image.");
            return model;
        }
        internal RestPoseLayers(MotionFrame original) {
            width=original.Width;height=original.Height;
            if(width!=512 || height!=512)throw new InvalidDataException("School2 rest tracing expects the original 512px canvas.");
            string[] names={"body","upper-arm","forearm","hand"};
            var bytes=new Dictionary<string,byte[]>();foreach(string name in names)bytes[name]=new byte[original.Pixels.Length];
            // Trace follows the sleeve's inside seam and the visible hand behind
            // the skirt. Coordinates are in the ORIGINAL image, never its bbox.
            Point[] outline={new Point(299,125),new Point(309,130),new Point(316,151),new Point(314,170),new Point(309,190),new Point(312,207),new Point(311,218),new Point(304,238),new Point(298,238),new Point(293,218),new Point(287,209),new Point(282,201),new Point(285,187),new Point(288,163),new Point(289,143),new Point(292,131)};
            for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
                bool inArm=Inside(x+.5,y+.5,outline);
                // Below the cuff the skirt overlaps the forearm. Keep its dark
                // pixels in front; a polygon alone would cut a notch in the skirt.
                if(inArm && y>=217){
                    bool skin=false;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
                        int xx=x+dx,yy=y+dy;if(xx<0 || xx>=width || yy<0 || yy>=height)continue;int k=(yy*width+xx)*4;
                        if(original.Pixels[k+3]>=32 && original.Pixels[k+2]>100 && original.Pixels[k+2]>original.Pixels[k]*1.12 && original.Pixels[k+1]>original.Pixels[k]*1.03)skin=true;
                    }
                    inArm=skin;
                }
                string owner=!inArm?"body":y<202?"upper-arm":y<217?"forearm":"hand";
                int at=(y*width+x)*4;Buffer.BlockCopy(original.Pixels,at,bytes[owner],at,4);
            }
            foreach(string name in names)Layers[name]=Frame(bytes[name]);
        }
        private static bool Inside(double x,double y,Point[] points){
            bool inside=false;for(int i=0,j=points.Length-1;i<points.Length;j=i++)if((points[i].Y>y)!=(points[j].Y>y) && x<(points[j].X-points[i].X)*(y-points[i].Y)/(points[j].Y-points[i].Y)+points[i].X)inside=!inside;return inside;
        }
        private MotionFrame Frame(byte[] pixels){var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);image.Freeze();return new MotionFrame(image);}
        internal MotionFrame Compose(){
            // Disjoint ownership preserves even antialiasing/transparent RGB bytes.
            // Do not return the original image: this rebuilds from actual layer data.
            byte[] result=new byte[width*height*4];
            foreach(var entry in Layers){byte[] p=entry.Value.Pixels;for(int i=0;i<p.Length;i++)result[i]|=p[i];}
            return Frame(result);
        }
        internal void Save(string directory){
            Directory.CreateDirectory(directory);
            foreach(var entry in Layers)SaveImage(entry.Value.Image,Path.Combine(directory,entry.Key+".png"));
            SaveImage(Compose().Image,Path.Combine(directory,"rest-composite.png"));
        }
        private static void SaveImage(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);}
    }
}
