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
                // Preserve settings accepted by earlier public releases. Explicit choices
                // take precedence over portable discovery; stale choices surface an error.
                string configured=Environment.GetEnvironmentVariable("ANNA_ASR_ENGINE");
                if(!String.IsNullOrWhiteSpace(configured))return ResolveConfiguredEngine(configured);
                string registration=Path.Combine(Program.DataDir,"asr-engine-path.txt");
                if(File.Exists(registration))return ResolveConfiguredEngine(File.ReadAllText(registration));
                return ResolveEngine(AppDomain.CurrentDomain.BaseDirectory);
            }
        }
        private static string ResolveConfiguredEngine(string value) {
            string path=(value??"").Trim();
            if(path.Length==0 || !Path.IsPathRooted(path) || !ValidEngine(path))
                throw new InvalidOperationException("本地中文识别引擎登记无效。请更新 ANNA_ASR_ENGINE 或设置目录 asr-engine-path.txt，填写包含 api_v2.py 与 runtime/python.exe 的完整引擎路径。");
            return Path.GetFullPath(path);
        }
        internal static string ResolveEngine(string baseDir) {
            if(String.IsNullOrWhiteSpace(baseDir))throw new InvalidOperationException("无法确定本地语音引擎位置：程序目录为空。");
            try {
                DirectoryInfo current=new DirectoryInfo(Path.GetFullPath(baseDir));
                // Local registration has precedence. A broken registration is an actionable
                // configuration error, never permission to silently load an unrelated model.
                for(int level=0;current!=null && level<6;level++,current=current.Parent) {
                    string voiceDir=Path.Combine(current.FullName,"voice-engine");
                    string registration=Path.Combine(voiceDir,"engine-path.txt");
                    if(File.Exists(registration)) {
                        string configured=File.ReadAllText(registration).Trim();
                        if(configured.Length==0)throw new InvalidOperationException("本地语音引擎登记为空，请重新登记 voice-engine/engine-path.txt。");
                        string resolved=Path.GetFullPath(Path.IsPathRooted(configured)?configured:Path.Combine(voiceDir,configured));
                        if(!ValidEngine(resolved))throw new InvalidOperationException("本地语音引擎登记无效，缺少 api_v2.py 或 runtime/python.exe。请更新 voice-engine/engine-path.txt："+registration);
                        return resolved;
                    }
                    // Support official portable packages beside the pet or inside voice-engine.
                    var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string embedded=Path.Combine(voiceDir,"GPT-SoVITS");
                    if(ValidEngine(embedded))candidates.Add(Path.GetFullPath(embedded));
                    if(current.Exists)foreach(string candidate in Directory.GetDirectories(current.FullName,"GPT-SoVITS*",SearchOption.TopDirectoryOnly))
                        if(ValidEngine(candidate))candidates.Add(Path.GetFullPath(candidate));
                    if(candidates.Count>1)throw new InvalidOperationException("找到多个本地语音引擎，请在 voice-engine/engine-path.txt 中明确登记要使用的引擎。");
                    foreach(string candidate in candidates)return candidate;
                }
            } catch(IOException ex) { throw new InvalidOperationException("无法读取本地语音引擎目录或登记文件，请检查路径及文件占用。",ex); }
              catch(UnauthorizedAccessException ex) { throw new InvalidOperationException("没有权限读取本地语音引擎目录或登记文件。",ex); }
              catch(ArgumentException ex) { throw new InvalidOperationException("本地语音引擎登记路径格式无效，请检查 voice-engine/engine-path.txt。",ex); }
              catch(NotSupportedException ex) { throw new InvalidOperationException("本地语音引擎登记路径格式无效，请检查 voice-engine/engine-path.txt。",ex); }
            throw new InvalidOperationException("未找到完整的本地语音引擎，请在运行目录 voice-engine/engine-path.txt 登记包含 api_v2.py 与 runtime/python.exe 的引擎目录。");
        }
        private static bool ValidEngine(string path) {
            return File.Exists(Path.Combine(path,"api_v2.py")) && File.Exists(Path.Combine(path,"runtime","python.exe"));
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
                    string engine=Engine;
                    var info=new ProcessStartInfo(Path.Combine(engine,"runtime","python.exe"),"-u \""+Path.Combine(Path.GetDirectoryName(typeof(AsrWorker).Assembly.Location),"local_asr.py")+"\" \""+engine+"\" --server"){WorkingDirectory=engine,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
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
