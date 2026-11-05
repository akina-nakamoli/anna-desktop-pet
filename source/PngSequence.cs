using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;
namespace AnnaDesktopPet {
 internal sealed class PngFrameInfo {public string file{get;set;}public int durationMs{get;set;}}
 internal sealed class PngSequenceInfo {public int formatVersion{get;set;}public int canvasWidth{get;set;}public int canvasHeight{get;set;}public bool loop{get;set;}public string source{get;set;}public PngFrameInfo[] frames{get;set;}}
 internal sealed class PngSequence {
  private static readonly object sharedGate=new object();private static readonly Dictionary<string,WeakReference> sharedPacks=new Dictionary<string,WeakReference>(StringComparer.OrdinalIgnoreCase);
  private readonly PngSequenceInfo info;private readonly string[] paths;private readonly int[] ends;private readonly MotionFrame idle;private readonly object gate=new object();
  private readonly Dictionary<int,MotionFrame> cache=new Dictionary<int,MotionFrame>();private readonly LinkedList<int> recent=new LinkedList<int>();private readonly HashSet<int> pending=new HashSet<int>();
  private readonly MotionFrame[] prepared;private readonly int preparedCount;private int decodedFrames;
  internal bool IsPreloaded{get{return prepared!=null;}} internal int CacheLimit{get{return prepared!=null?preparedCount:8;}} internal int DecodedFrames{get{return decodedFrames;}}
  internal int Count{get{return paths.Length;}} internal int DurationMs{get{return ends[ends.Length-1];}} internal int CachedCount{get{if(prepared!=null)return preparedCount;lock(gate)return cache.Count;}} internal string Source{get{return info.source??"";}}
  internal PngSequence(string directory,MotionFrame reference){
   idle=reference;info=new JavaScriptSerializer().Deserialize<PngSequenceInfo>(File.ReadAllText(Path.Combine(directory,"animation.json")));
   if(info==null || info.formatVersion!=1 || info.frames==null || info.frames.Length<2 || info.frames.Length>600)throw new InvalidDataException("Invalid PNG sequence manifest");
   if(info.canvasWidth!=reference.Width || info.canvasHeight!=reference.Height)throw new InvalidDataException("PNG sequence canvas does not match outfit");
   string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;paths=new string[info.frames.Length];ends=new int[paths.Length];int end=0;
   for(int i=0;i<paths.Length;i++){
    var f=info.frames[i];if(f==null || String.IsNullOrWhiteSpace(f.file) || f.durationMs<8 || f.durationMs>5000)throw new InvalidDataException("Invalid PNG frame timing");
    string path=Path.GetFullPath(Path.Combine(root,f.file));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))throw new InvalidDataException("Invalid PNG frame path");
    paths[i]=path;end=checked(end+f.durationMs);ends[i]=end;
   }
   if(end>120000)throw new InvalidDataException("PNG action is too long");
   var unique=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
   // Short actions are decoded once before playback. Reversed return frames
   // reuse the same object; there is no file IO or cache eviction mid-gesture.
   if(paths.Length<=64 && unique.Count<=32 && (long)unique.Count*info.canvasWidth*info.canvasHeight*8<=384L*1024*1024){
    string key=root+File.GetLastWriteTimeUtc(Path.Combine(root,"animation.json")).Ticks;
    lock(sharedGate){
     WeakReference weak;MotionFrame[] shared=null;if(sharedPacks.TryGetValue(key,out weak))shared=weak.Target as MotionFrame[];
     if(shared!=null && shared.Length==Count){prepared=shared;preparedCount=unique.Count;}
     else{
      var all=new MotionFrame[Count];var images=new Dictionary<string,MotionFrame>(StringComparer.OrdinalIgnoreCase);
      for(int i=0;i<Count;i++){MotionFrame image;if(!images.TryGetValue(paths[i],out image)){image=Read(i);images.Add(paths[i],image);}all[i]=image;}
      prepared=all;preparedCount=images.Count;if(sharedPacks.Count>32)sharedPacks.Clear();sharedPacks[key]=new WeakReference(all);
     }
    }
    cache.Clear();recent.Clear();
   }else{Read(0);Prefetch(0);}
  }
  private MotionFrame Read(int index){if(prepared!=null)return prepared[index];lock(gate){
   MotionFrame frame;if(cache.TryGetValue(index,out frame)){recent.Remove(index);recent.AddLast(index);return frame;}
   var image=new BitmapImage();using(var stream=File.OpenRead(paths[index])){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();}
   if(image.PixelWidth!=info.canvasWidth || image.PixelHeight!=info.canvasHeight)throw new InvalidDataException("PNG frame dimensions differ: "+paths[index]);
   frame=new MotionFrame(image);decodedFrames++;cache[index]=frame;recent.AddLast(index);while(cache.Count>8){int oldest=recent.First.Value;recent.RemoveFirst();cache.Remove(oldest);}return frame;
  }}
  private void Prefetch(int index){if(prepared!=null)return;for(int ahead=1;ahead<=2;ahead++){
   int next=index+ahead;if(next>=Count){if(!info.loop)break;next%=Count;}
   lock(gate){if(cache.ContainsKey(next) || pending.Contains(next) || pending.Count>=3)continue;pending.Add(next);}
   int key=next;ThreadPool.QueueUserWorkItem(delegate{try{Read(key);}catch(Exception ex){Program.Log("PNG prefetch: "+ex.Message);}finally{lock(gate)pending.Remove(key);}});
  }}
  internal MotionFrame GetAt(double seconds){
   double ms=Math.Max(0,seconds)*1000;if(Double.IsNaN(ms) || Double.IsInfinity(ms))return idle;if(ms>=DurationMs){if(!info.loop)return idle;ms%=DurationMs;}
   int low=0,high=ends.Length-1;while(low<high){int mid=low+(high-low)/2;if(ms<ends[mid])high=mid;else low=mid+1;}MotionFrame frame=Read(low);Prefetch(low);return frame;
  }
 }
}
