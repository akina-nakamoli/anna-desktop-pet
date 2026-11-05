using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
namespace AnnaDesktopPet {
    internal sealed class AsrWorker : IDisposable {
        internal static string Engine {
            get {
                string path=Environment.GetEnvironmentVariable("ANNA_ASR_ENGINE");
                if(String.IsNullOrWhiteSpace(path)) {
                    string config=Path.Combine(Program.DataDir,"asr-engine-path.txt");
                    if(File.Exists(config))path=File.ReadAllText(config).Trim();
                }
                if(String.IsNullOrWhiteSpace(path)) {
                    string config=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voice-engine","engine-path.txt");
                    if(File.Exists(config))path=File.ReadAllText(config).Trim();
                }
                if(String.IsNullOrWhiteSpace(path) || !File.Exists(Path.Combine(path,"runtime","python.exe")))
                    throw new InvalidOperationException("本地中文识别模型未配置。请在设置说明中指定已安装的 GPT-SoVITS 路径。");
                return Path.GetFullPath(path);
            }
        }
        private readonly object gate=new object();
        private readonly SemaphoreSlim serial=new SemaphoreSlim(1,1);
        private Process process;
        private TaskCompletionSource<bool> ready;
        private TaskCompletionSource<string> result;
        private string requestId;
        private bool disposed;
        internal string Status {get;private set;}
        internal AsrWorker(){Status="模型尚未加载";}
        internal void WarmUp(){if(Program.Smoke)return;Task.Run(async delegate{try{await EnsureReady();}catch(Exception ex){Status=ex.Message;Program.Log("ASR warmup failed: "+ex.Message);}});}
        private async Task EnsureReady(){
            Task task;
            lock(gate){
                if(disposed)throw new ObjectDisposedException("AsrWorker");
                if(process==null || process.HasExited){
                    if(process!=null)process.Dispose();
                    ready=new TaskCompletionSource<bool>();Status="正在加载本地中文模型…";
                    var info=new ProcessStartInfo(Path.Combine(Engine,"runtime","python.exe"),"-u \""+Path.Combine(Path.GetDirectoryName(typeof(AsrWorker).Assembly.Location),"local_asr.py")+"\" \""+Engine+"\" --server"){WorkingDirectory=Engine,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    process=new Process{StartInfo=info,EnableRaisingEvents=true};
                    process.OutputDataReceived+=OnOutput;
                    process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null && e.Data.Contains("Error"))Program.Log("ASR: "+e.Data);};
                    process.Exited+=delegate(object sender,EventArgs e){lock(gate){if(!disposed && Object.ReferenceEquals(sender,process)){Status="识别服务已退出，下次使用会重新加载";ready.TrySetException(new IOException(Status));if(result!=null)result.TrySetException(new IOException(Status));}}};
                    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                }
                task=ready.Task;
            }
            if(await Task.WhenAny(task,Task.Delay(120000))!=task)throw new TimeoutException("中文模型加载超时。");
            await task;
        }
        private void OnOutput(object sender,DataReceivedEventArgs e){
            if(e.Data==null)return;
            lock(gate){
                if(disposed || !Object.ReferenceEquals(sender,process))return;
                if(e.Data.StartsWith("ANNA_READY:")){Status="本地中文模型已就绪";ready.TrySetResult(true);}
                else if(e.Data.StartsWith("ANNA_RESULT:") && result!=null){
                    try{var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.Data.Substring(12));if(Convert.ToString(data["id"])!=requestId)return;if(data.ContainsKey("error"))result.TrySetException(new IOException("本地识别失败："+data["error"]));else result.TrySetResult(Convert.ToString(data["text"]).Trim());}catch(Exception ex){result.TrySetException(ex);}
                }
            }
        }
        internal async Task<string> Recognize(string audio){
            await serial.WaitAsync();
            try{await EnsureReady();Task<string> pending;
                lock(gate){if(disposed)throw new ObjectDisposedException("AsrWorker");result=new TaskCompletionSource<string>();requestId=Guid.NewGuid().ToString("N");pending=result.Task;process.StandardInput.WriteLine(new JavaScriptSerializer().Serialize(new {id=requestId,audio=audio}));process.StandardInput.Flush();}
                if(await Task.WhenAny(pending,Task.Delay(90000))!=pending)throw new TimeoutException("本地识别超时。");
                return await pending;
            }finally{lock(gate){result=null;requestId=null;}serial.Release();}
        }
        public void Dispose(){Process child;lock(gate){if(disposed)return;disposed=true;if(ready!=null)ready.TrySetCanceled();if(result!=null)result.TrySetCanceled();child=process;process=null;Status="识别服务已关闭";}if(child!=null){try{if(!child.HasExited){child.StandardInput.WriteLine("{\"command\":\"quit\"}");child.StandardInput.Flush();if(!child.WaitForExit(300)){child.Kill();child.WaitForExit(3000);}}}catch{}finally{child.Dispose();}}}
    }
}
