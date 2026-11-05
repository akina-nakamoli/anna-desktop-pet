using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
namespace AnnaDesktopPet {
    internal sealed class LocalAsr : IDisposable {
        private readonly AsrWorker worker;
        private Microphone microphone;
        internal static int Device=-1;
        internal double Level {get{return microphone==null?0:microphone.Level;}}
        internal LocalAsr(AsrWorker service){worker=service;}
        private string audio;
        internal void Start(){
            Directory.CreateDirectory(Program.DataDir);audio=Path.Combine(Program.DataDir,"voice-"+Guid.NewGuid().ToString("N")+".wav");
            try{microphone=new Microphone();microphone.Start(Device);}catch{Dispose();throw;}
        }
        internal async Task<string> Finish(){
            microphone.Save(audio);microphone.Dispose();microphone=null;
            try{return await worker.Recognize(audio);}finally{Dispose();}
        }
        public void Dispose(){if(microphone!=null){microphone.Dispose();microphone=null;}if(audio!=null && File.Exists(audio)){try{File.Delete(audio);}catch{}}}
    }
}
