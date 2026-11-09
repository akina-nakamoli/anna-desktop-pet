using System;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace AnnaDesktopPet {
    internal static class VoiceAutoStart {
        private static string VoiceFolder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voice-engine"); } }

        internal static Task PrepareReferenceAsync(string engineDirectory, int port, CancellationToken token)
        {
            return Task.Run(delegate
            {
                using (var gate = new Mutex(false, "Local\\AnnaDesktopPet-VoiceAutoStart-" + port))
                {
                    bool held = false;
                    try
                    {
                        try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                        if (!held) throw new InvalidOperationException("本地音色服务正在启动，请稍后再试。");
                        token.ThrowIfCancellationRequested();
                        bool alreadyListening = Listening(port);
                        string script = Path.Combine(VoiceFolder, "Start-VoiceEngine.ps1");
                        if (!File.Exists(script)) throw new InvalidOperationException("找不到音色启动脚本，请完整解压发布包。");
                        string arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -EnginePath \"" +
                            engineDirectory + "\" -Port " + port + (alreadyListening ? " -CheckOnly" : "");
                        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), arguments)
                        {
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true
                        };
                        using (var child = Process.Start(info))
                        {
                            if (child == null) throw new InvalidOperationException("无法启动音色检查脚本。");
                            Task<string> output = child.StandardOutput.ReadToEndAsync();
                            Task<string> errors = child.StandardError.ReadToEndAsync();
                            DateTime deadline = DateTime.UtcNow.AddMinutes(3);
                            while (!child.WaitForExit(250))
                            {
                                if (token.IsCancellationRequested || DateTime.UtcNow >= deadline)
                                {
                                    try { child.Kill(); } catch (InvalidOperationException) { }
                                    token.ThrowIfCancellationRequested();
                                    throw new InvalidOperationException("GPT-SoVITS 环境检查超时。");
                                }
                            }
                            if (child.ExitCode != 0)
                            {
                                string detail = output.GetAwaiter().GetResult().Trim();
                                string error = errors.GetAwaiter().GetResult().Trim();
                                if (detail.Length == 0) detail = error;
                                if (detail.Length > 650) detail = detail.Substring(0, 650);
                                throw new InvalidOperationException("GPT-SoVITS 未能准备好。请检查整合包、模型和显卡配置。\n" + detail);
                            }
                        }
                        DateTime readyDeadline = DateTime.UtcNow.AddMinutes(3);
                        while (!Listening(port))
                        {
                            token.ThrowIfCancellationRequested();
                            if (DateTime.UtcNow >= readyDeadline) throw new InvalidOperationException("音色服务未就绪。请查看 voice-engine/logs 下最新的启动日志。");
                            token.WaitHandle.WaitOne(500);
                        }
                        token.ThrowIfCancellationRequested();
                    }
                    finally { if (held) gate.ReleaseMutex(); }
                }
            }, token);
        }

        internal static void RememberReferenceEngine(string engineDirectory)
        {
            File.WriteAllText(Path.Combine(VoiceFolder, "engine-path.txt"), engineDirectory, new System.Text.UTF8Encoding(false));
        }

        internal static void Start(SpeechSettings settings) {
            if(Program.Smoke || settings.Engine!="gpt-sovits")return;
            Uri endpoint;
            if(!Uri.TryCreate(settings.Endpoint,UriKind.Absolute,out endpoint) || endpoint.Scheme!="http" || (endpoint.Host!="127.0.0.1" && endpoint.Host!="localhost"))return;
            Task.Run(delegate {
                using(var gate=new Mutex(false,"Local\\AnnaDesktopPet-VoiceAutoStart-"+endpoint.Port)) {
                    bool held=false;
                    try {
                        try { held=gate.WaitOne(0); }catch(AbandonedMutexException){held=true;}
                        if(!held || Listening(endpoint.Port))return;
                        string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"voice-engine","Start-VoiceEngine.ps1");
                        if(!File.Exists(script)){Program.Log("Voice autostart: startup script missing.");return;}
                        using(var process=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -Port "+endpoint.Port) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
                            if(process==null)return;
                            if(!process.WaitForExit(60000)){Program.Log("Voice autostart: preflight is still running.");return;}
                            if(process.ExitCode!=0){Program.Log("Voice autostart failed; check voice-engine logs or start manually.");return;}
                        }
                        for(int i=0;i<120;i++){if(Listening(endpoint.Port)){Program.Log("Voice autostart: local port is ready.");return;}Thread.Sleep(1000);}
                        Program.Log("Voice autostart: model loading timed out; check voice-engine logs.");
                    }catch(Exception){Program.Log("Voice autostart could not complete.");}
                    finally {if(held)gate.ReleaseMutex();}
                }
            });
        }
        private static bool Listening(int port) {
            using(var client=new TcpClient()) {try {var task=client.ConnectAsync("127.0.0.1",port);return task.Wait(500)&&client.Connected;}catch{return false;}}
        }
    }
}
