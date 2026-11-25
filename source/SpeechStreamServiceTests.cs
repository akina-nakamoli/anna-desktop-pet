using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal static class SpeechStreamServiceTests
    {
        private static int checks;
        private static void Check(bool good,string name){if(!good)throw new Exception(name);checks++;}
        private static void Call(SpeechService service,string method,params object[] args)
        {
            MethodInfo entry=typeof(SpeechService).GetMethod(method);
            Check(entry!=null,"SpeechService exposes "+method); entry.Invoke(service,args);
        }
        private static void Wait(Func<bool> condition,string label)
        {
            DateTime end=DateTime.UtcNow.AddSeconds(4);
            while(!condition() && DateTime.UtcNow<end){Pump();Thread.Sleep(5);}Pump();Check(condition(),label);
        }
        private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
        private sealed class Handler:HttpMessageHandler
        {
            internal readonly List<string> Texts=new List<string>();
            internal readonly List<CancellationToken> Tokens=new List<CancellationToken>();
            internal readonly List<TaskCompletionSource<HttpResponseMessage>> Pending=new List<TaskCompletionSource<HttpResponseMessage>>();
            internal bool Automatic;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                string body=await request.Content.ReadAsStringAsync();
                var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(body);
                TaskCompletionSource<HttpResponseMessage> wait=new TaskCompletionSource<HttpResponseMessage>();
                lock(Texts){Texts.Add((string)json["text"]);Tokens.Add(token);Pending.Add(wait);}
                if(Automatic)wait.SetResult(Response());
                return await wait.Task;
            }
            internal int Count{get{lock(Texts)return Texts.Count;}}
            internal void Finish(int n){lock(Texts)Pending[n].SetResult(Response());}
        }
        private static HttpResponseMessage Response(){return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Wave())};}
        private static byte[] Wave()
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(38);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(24000);writer.Write(48000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(2);writer.Write((short)0);return stream.ToArray();}
        }
        private static byte[] LevelWave(int sampleRate, int channels, params double[] amplitudes)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int totalFrames = amplitudes.Length * sampleRate / 50;
                int dataSize = totalFrames * channels * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
                writer.Write(sampleRate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataSize);
                for (int i = 0; i < amplitudes.Length; i++)
                    for (int sample = i * sampleRate / 50; sample < (i + 1) * sampleRate / 50; sample++)
                        for (int channel = 0; channel < channels; channel++)
                            writer.Write((short)(amplitudes[i] * 32767 * ((sample + channel) % 2 == 0 ? 1 : -1)));
                return stream.ToArray();
            }
        }

        private static void VerifyMouthWindows()
        {
            byte[] wave = LevelWave(11025, 2, 0, .004, .034, .035, .18, .21, 0, .034, 0, 0);
            float[] levels = SpeechService.ReadMouthLevels(wave);
            float[] expected = { 0, 0, .5f, .5f, 1, 1, 0, .5f, 0, 0 };
            Check(levels != null && levels.Length == expected.Length, "20 ms indexing supports fractional sample counts");
            for (int i = 0; i < expected.Length; i++)
                Check(levels[i] == expected[i], "stereo energy and 20 ms pause, window " + i);

            double[] nearBoundary = { .22, .056, .055, .056, .055, .056, .055, .035, .032, .019, .015, .016, .007 };
            levels = SpeechService.ReadMouthLevels(LevelWave(22050, 1, nearBoundary));
            for (int i = 0; i < 7; i++) Check(levels[i] == 1, "open mouth stays steady around upper entry threshold");
            Check(levels[8] == .5f && levels[11] == .5f && levels[12] == 0, "half mouth holds through weak syllable, then closes");
            float[] muted = SpeechService.MouthLevelsAtVolume(wave, 0);
            Check(Array.TrueForAll(muted, value => value == 0), "muted output closes all windows");
            float[] quiet = SpeechService.MouthLevelsAtVolume(wave, 20);
            Check(quiet[3] == 0 && quiet[5] == .5f, "lower playback gain is applied before the thresholds");
            Check(SpeechService.ReadMouthLevels(null) == null && SpeechService.ReadMouthLevels(new byte[6]) == null, "invalid clips have no mouth timeline");
            long onset, end;
            byte[] phrase = LevelWave(24000,2,0,0,.20,0,.20,0);
            levels = SpeechService.MouthLevelsAtVolume(phrase,100,out onset,out end);
            long window = TimeSpan.TicksPerSecond/50;
            Check(onset == 2*window && end == 5*window, "PCM voice boundaries use stereo sample times");
            Check(!PortraitPlayback.FromWave(true,false,onset, onset,levels,end).Audible &&
                !PortraitPlayback.FromWave(true,true,onset-1, onset,levels,end).Audible,
                "queued audio and the leading silence cannot start a speaking motion");
            var first = PortraitPlayback.FromWave(true,true,onset,onset,levels,end);
            var pause = PortraitPlayback.FromWave(true,true,3*window,onset,levels,end);
            Check(first.Audible && first.Mouth == 1 && pause.Audible && pause.Mouth == 0,
                "actual sound starts the motion and a pause closes only the lips");
            Check(!PortraitPlayback.FromWave(true,true,end,onset,levels,end).Audible,
                "trailing silence ends the phrase even while the player is open");
            SpeechService.MouthLevelsAtVolume(phrase,0,out onset,out end);
            Check(onset == -1 && !PortraitPlayback.FromWave(true,true,2*window,onset,levels,end).Audible,
                "a muted player never starts the speaking motion");
        }

        private static void VerifyWindowsPcm()
        {
            using (var service = new SpeechService(Dispatcher.CurrentDispatcher))
            {
                if (!service.Available)
                {
                    Console.WriteLine("SKIP real Japanese SAPI: no usable installed Japanese voice");
                    return;
                }
                string name = service.Voices[0].Name;
                using (var cancel = new CancellationTokenSource())
                {
                    Task<byte[]> old = SpeechService.GenerateWindowsWave(name, 0, new string('あ', 100), cancel.Token);
                    cancel.Cancel();
                    Task<byte[]> fresh = SpeechService.GenerateWindowsWave(name, 0, "こんにちは、杏菜だよ。", CancellationToken.None);
                    Wait(delegate { return old.IsCompleted && fresh.IsCompleted; }, "cancelled and replacement Windows PCM workers settle");
                    Check(old.IsCanceled, "cancelled Windows PCM worker cannot play a stale phrase");
                    float[] levels = SpeechService.ReadMouthLevels(fresh.GetAwaiter().GetResult());
                    Check(levels != null && Array.IndexOf(levels, 0f) >= 0 && Array.IndexOf(levels, .5f) >= 0 && Array.IndexOf(levels, 1f) >= 0,
                        "real Japanese Windows voice produces all three mouth shapes from PCM");
                    Console.WriteLine("PASS real Japanese Windows voice PCM: " + name + ", " + levels.Length + " energy windows");
                }
                // No speakers: cancel before playback dispatch. Both paths must immediately release their identity.
                service.Speak("これは取り消します。");
                Check(service.IsBusy && !service.IsSpeaking && service.MouthOpen == 0, "Windows synthesis waits for playback before opening the mouth");
                service.Stop();
                Check(!service.IsBusy && !service.IsSpeaking && service.MouthOpen == 0, "stop closes the normal Windows voice synchronously");
                service.BeginStream(); service.AppendStream("最初の文です。"); service.CompleteStream(); service.Stop();
                Check(!service.IsBusy && service.MouthOpen == 0, "stop also clears streaming Windows synthesis");
            }
        }

        [STAThread]private static int Main()
        {            var emotional = SpeechService.SplitSpeech("[calm]そうなんだ。[happy]よかったね！");
            Check(emotional.Count == 2 && emotional[0] == "[calm]そうなんだ。" && emotional[1] == "[happy]よかったね！",
                "Emotion can change at a sentence boundary without stacked control labels");
            string temp=Path.Combine(Path.GetTempPath(),"Anna-stream-speech-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                VerifyMouthWindows();
                VerifyWindowsPcm();
                string reference=Path.Combine(temp,"reference.wav"),output=Path.Combine(temp,"output.wav");File.WriteAllBytes(reference,Wave());
                var handler=new Handler();
                using(var service=new SpeechService(Dispatcher.CurrentDispatcher,output,new GptSovitsClient(handler)))
                {
                    service.ConfigureBackend("gpt-sovits","http://127.0.0.1:9880",reference,"こんにちは。");
                    Call(service,"BeginStream");long operation=service.OperationId;
                    Call(service,"AppendStream","こんにちは。");Wait(delegate{return handler.Count==1;},"first real service request before model completion");
                    Call(service,"AppendStream","また会えたね。");
                    Check(service.OperationId==operation,"append keeps same service generation");Check(!handler.Tokens[0].IsCancellationRequested,"append does not cancel synthesis");Check(handler.Count==1,"sentence synthesis ordered");
                    Call(service,"CompleteStream");handler.Finish(0);Wait(delegate{return handler.Count==2;},"second sentence generated after first");
                    Check(service.IsBusy,"draining queue remains busy");handler.Finish(1);Wait(delegate{return !service.IsBusy;},"service finishes entire stream");Check(File.Exists(output),"offline WAV output written");
                    Check(handler.Texts[0]=="こんにちは。" && handler.Texts[1]=="また会えたね。","service preserves order and Japanese text");
                    Call(service,"AppendStream","遅れた文。");Pump();Check(handler.Count==2,"completed stream ignores late append");

                    Call(service,"BeginStream");Call(service,"AppendStream","古い文。");Wait(delegate{return handler.Count==3;},"old stream request starts");Call(service,"AppendStream","捨てる文。");
                    service.Stop();Check(handler.Tokens[2].IsCancellationRequested,"stop aborts old local TTS");Check(!service.IsBusy,"stop clears busy immediately");
                    Call(service,"AppendStream","届いた古い返答。");Call(service,"CompleteStream");handler.Finish(2);
                    Call(service,"BeginStream");Call(service,"AppendStream","新しい文。");Call(service,"CompleteStream");Wait(delegate{return handler.Count==4;},"new stream works after stop");handler.Finish(3);
                    Wait(delegate{return !service.IsBusy;},"new stream completes");Check(handler.Count==4,"old queued sentences never revive");Check(handler.Texts[3]=="新しい文。","new utterance identity preserved");

                    handler.Automatic=true;Call(service,"BeginStream");Call(service,"AppendStream",new string('あ',1490)+"。");Call(service,"AppendStream","ありがとう。また遊ぼうね。");Call(service,"CompleteStream");Wait(delegate{return !service.IsBusy;},"bounded long stream completes");
                    int total=0;for(int i=4;i<handler.Count;i++)total+=handler.Texts[i].Length;Check(total<=1500,"stream-wide speech budget preserved");Check(total>=1491,"budget retains initial speech");
                    Call(service,"BeginStream");Call(service,"CompleteStream");Wait(delegate{return !service.IsBusy;},"empty service stream completes");

                    handler.Automatic=false;int oldCount=handler.Count;
                    Call(service,"BeginStream");Call(service,"AppendStream","設定前の文。");Call(service,"AppendStream","続けてはいけない。");
                    Wait(delegate{return handler.Count==oldCount+1;},"configure cancellation request starts");
                    service.ConfigureBackend("gpt-sovits","http://127.0.0.1:9880",reference,"こんにちは。");
                    Check(handler.Tokens[oldCount].IsCancellationRequested,"changing backend configuration cancels stream");Check(!service.IsBusy,"configuration clears stream state");
                    handler.Finish(oldCount);Pump();Thread.Sleep(30);Pump();Check(handler.Count==oldCount+1,"late completion after configuration cannot replay queue");

                    oldCount=handler.Count;Call(service,"BeginStream");Call(service,"AppendStream","サービス失敗。");Call(service,"AppendStream","失敗後の文。");Call(service,"CompleteStream");
                    Wait(delegate{return handler.Count==oldCount+1;},"failure fixture starts");
                    lock(handler.Texts)handler.Pending[oldCount].SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("offline test")});
                    Wait(delegate{return !service.IsBusy;},"backend failure settles queue");Check(service.Status.Contains("503"),"backend failure visible to user");Check(handler.Count==oldCount+1,"failure does not play later sentences");

                    oldCount=handler.Count;Call(service,"BeginStream");Call(service,"AppendStream","古いストリーム。");Wait(delegate{return handler.Count==oldCount+1;},"normal speak replacement fixture starts");
                    service.Speak("通常の音声。");Wait(delegate{return handler.Count==oldCount+2;},"ordinary speak still starts");
                    Check(handler.Tokens[oldCount].IsCancellationRequested,"ordinary speak supersedes old stream");handler.Finish(oldCount);handler.Finish(oldCount+1);Wait(delegate{return !service.IsBusy;},"ordinary speak completes after replacement");
                }
                Console.WriteLine("PASS speech service stream: "+checks+" assertions");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL: "+error.GetBaseException().Message);return 1;}
            finally{foreach(string file in Directory.GetFiles(temp))File.Delete(file);Directory.Delete(temp);}
        }
    }
}
