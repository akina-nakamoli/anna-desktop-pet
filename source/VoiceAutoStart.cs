using System;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace AnnaDesktopPet {
    internal static class VoiceAutoStart {
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
