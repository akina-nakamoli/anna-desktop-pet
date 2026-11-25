using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnnaDesktopPet
{
    internal sealed partial class PetWindow
    {
        // Identical clocks across the three previews. The reference WAV has a real silent lead-in.
        private const int ConversationFps = 24, ConversationFrames = 144;
        private const int PlaybackBegins = 60, PlaybackEnds = 120;

        private string VerifyPortraitConversationSmoke()
        {
            string original = Appearance.Outfit;
            bool enabled = mediaSettings.MotionsEnabled, autonomousEnabled = mediaSettings.AutonomousEnabled;
            bool ticking = tick.IsEnabled;
            string preview = Program.PortraitConversationPreviewDirectory;
            string wav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reference","anna.wav");
            byte[] recording = File.ReadAllBytes(wav);
            long firstSound, lastSound, mutedStart;
            float[] levels = SpeechService.MouthLevelsAtVolume(recording,100,out firstSound,out lastSound);
            SpeechService.MouthLevelsAtVolume(recording,0,out mutedStart);
            if (levels==null || Array.IndexOf(levels,.5f)<0 || Array.IndexOf(levels,1f)<0 ||
                firstSound<=0 || firstSound>=TimeSpan.TicksPerSecond*2 || mutedStart>=0)
                throw new Exception("The test recording must have leading silence, three mouth levels and no audible onset when muted.");
            if (PortraitPlayback.FromWave(true,true,firstSound-1,firstSound,levels,lastSound).Audible ||
                !PortraitPlayback.FromWave(true,true,firstSound,firstSound,levels,lastSound).Audible ||
                PortraitPlayback.FromWave(true,true,lastSound,firstSound,levels,lastSound).Audible ||
                PortraitPlayback.FromWave(true,false,firstSound,firstSound,levels,lastSound).Audible ||
                PortraitPlayback.FromWave(true,true,firstSound,mutedStart,levels).Audible)
                throw new Exception("Speech motion did not honor the PCM onset, tail, stop or mute boundary.");
            int firstFrame=PlaybackBegins+(int)Math.Ceiling(firstSound*(double)ConversationFps/TimeSpan.TicksPerSecond);
            // Submitting/completing the offline chat pumps WPF continuations. The deterministic
            // timeline below supplies the only animation clock during these checks.
            tick.Stop();
            CompositionTarget.Rendering -= Animate;
            try
            {
                mediaSettings.MotionsEnabled=true;
                mediaSettings.AutonomousEnabled=false;
                VerifyConversationTake(Appearance.Uniform1Portrait,"uniform1",levels,firstSound,firstFrame,preview);
                VerifyConversationTake(Appearance.Uniform2Portrait,"uniform2",levels,firstSound,firstFrame,preview);
                VerifyConversationTake(Appearance.GhostPortrait,"ghost",levels,firstSound,firstFrame,preview);
                if (preview!=null)
                {
                    var timeline=new { fps=ConversationFps, frames=ConversationFrames, playbackStartFrame=PlaybackBegins,
                        speakingStartFrame=firstFrame, playbackEndFrame=PlaybackEnds, audioOffsetSeconds=0 };
                    File.WriteAllText(Path.Combine(preview,"timeline.json"),new JavaScriptSerializer().Serialize(timeline));
                }
            }
            finally
            {
                mediaSettings.MotionsEnabled=enabled;
                mediaSettings.AutonomousEnabled=autonomousEnabled;
                poseUntil=0;
                bubble.Visibility=Visibility.Collapsed;
                SelectSmokeOutfit(original);
                CompositionTarget.Rendering += Animate;
                if (ticking) tick.Start();
            }
            return "Portrait conversation: three native outfits, real offline chat submission/completion/interruption, audio onset and end, silent buffering, authored gaze, voice-driven lips and neutral return verified";
        }

        private static HttpResponseMessage ConversationSmokeReply()
        {
            const string reply="窗外的云正好飘过。";
#if DEEPSEEK
            var doc=new { choices=new[]{new { message=new { content=reply }, finish_reason="stop" }} };
#else
            var doc=new { status="completed", output=new[]{new { type="message",content=new[]{new { type="output_text",text=reply }} }} };
#endif
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(new JavaScriptSerializer().Serialize(doc),Encoding.UTF8,"application/json")};
        }

        private static void AwaitConversationSmoke(Task task)
        {
            var expires=DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow>=expires) throw new Exception("Local conversation callback did not complete.");
                var frame=new DispatcherFrame();
                var wake=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(5)};
                wake.Tick+=delegate { wake.Stop(); frame.Continue=false; };
                wake.Start();
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }

        private void VerifyConversationTake(string outfit,string folder,float[] levels,long onset,int firstFrame,string destination)
        {
            SelectSmokeOutfit(outfit);
            if (liveModel==null) throw new Exception("Missing conversational standing portrait: "+outfit);
            poseUntil=happyUntil=0;
            bubble.Visibility=Visibility.Collapsed;
            liveFace.Reset(); liveModel.ResetConversationMotion();
            idleGestures.Reset(); ghostGestures.Reset(); uniform2Director.Reset(); lastLiveFrame=-1;
            var response=new TaskCompletionSource<HttpResponseMessage>();
            var fake=new GptClient(delegate(HttpRequestMessage request,CancellationToken token) {
                var held=response;
                token.Register(delegate { held.TrySetCanceled(); });
                return held.Task;
            });
            var history=new List<ChatMessage>();
            var chat=new GptChatWindow(this,history,new GptSettings { ApiKey="offline-render-test" },Say,
                delegate(bool busy){thinking=busy;},null,null,delegate{return false;},null,null,null,fake);
            chatWindow=chat;
            BindPortraitChat(chat);
            string frames=destination==null?null:Path.Combine(destination,folder,"sequence");
            if (frames!=null) Directory.CreateDirectory(frames);
            try
            {
                chat.Show();
                AwaitConversationSmoke(chat.SubmitAsync("  "));
                double start=elapsed.Elapsed.TotalSeconds;
                var silent=new PortraitPlayback(false,false,0);
                AnimateAt(start-.04,new Vector(),false,silent);
                byte[] neutral=(byte[])liveModel.Pixels.Clone();
                if (portraitConversation!=PortraitConversationState.None)
                    throw new Exception("A blank chat draft started a gesture: "+outfit);
                Task request=chat.SubmitAsync("我们聊聊天吧。");
                if (request.IsCompleted || !thinking) throw new Exception("Offline model did not hold the conversation request.");
                var observedMouths=new HashSet<double>();
                int thinkingMotion=0, speakingMotion=0;
                using (var left=Live2DPetModel.Load(Appearance.ModelManifest))
                using (var right=Live2DPetModel.Load(Appearance.ModelManifest))
                {
                    var a=new LiveFaceTracking(new Random(73));
                    var b=new LiveFaceTracking(new Random(73));
                    for (int i=0;i<ConversationFrames;i++)
                    {
                        double seconds=start+i/(double)ConversationFps;
                        if (i==45)
                        {
                            response.SetResult(ConversationSmokeReply());
                            AwaitConversationSmoke(request);
                            if (thinking || history.Count<2 || history[history.Count-1].Role!="assistant")
                                throw new Exception("The accepted chat reply did not complete normally.");
                        }
                        long position=(long)(Math.Max(0,i-PlaybackBegins)*(double)TimeSpan.TicksPerSecond/ConversationFps);
                        bool playing=i>=PlaybackBegins && i<PlaybackEnds;
                        var sound=PortraitPlayback.FromWave(i>=36 && i<PlaybackEnds,playing,position,onset,levels);
                        var expected=i<firstFrame?PortraitConversationState.Thinking:
                                     i<PlaybackEnds?PortraitConversationState.Speaking:PortraitConversationState.None;
                        var pointer=i<PlaybackEnds ? new Vector(Math.Cos(i*.21),Math.Sin(i*.13)) : new Vector();
                        AnimateAt(seconds,pointer,false,sound);
                        if (portraitConversation!=expected || character.Source!=liveModel.Frame ||
                            !Object.ReferenceEquals(alphaPixels,liveModel.Pixels))
                            throw new Exception("Conversation state/portrait mismatch: "+outfit+" frame "+i+" expected "+expected+" got "+portraitConversation);
                        double mouth=playing && i>=firstFrame?levels[(int)(position/(TimeSpan.TicksPerSecond/50))]:0;
                        if (liveModel.RenderedMouth!=mouth)
                            throw new Exception("Rendered lips differ from the PCM playback window: "+outfit+" "+i);
                        if (expected==PortraitConversationState.Speaking) observedMouths.Add(liveModel.RenderedMouth);
                        if (i%12==0)
                        {
                            int lower=(Live2DPetModel.OverheadPadding+(int)(Live2DPetModel.PortraitHeight*.53))*liveModel.Frame.PixelWidth*4;
                            for(int p=lower;p<neutral.Length;p++)
                                if(Math.Abs(neutral[p]-liveModel.Pixels[p])>3)
                                    throw new Exception("Conversational motion moved the resting pose below the waist: "+outfit+" "+i);
                        }
                        if (expected!=PortraitConversationState.None)
                        {
                            left.RenderTracked(a,seconds,pointer,new IdleGesturePose(),mouth,true,new GhostGesturePose(),uniform2Director.Pose,expected);
                            right.RenderTracked(b,seconds,-pointer,new IdleGesturePose(),mouth,true,new GhostGesturePose(),uniform2Director.Pose,expected);
                            if (PixelDifferences(left.Pixels,right.Pixels,0)!=0)
                                throw new Exception("Cursor altered conversational gaze: "+outfit+" "+i);
                            int faceChange=ConversationFaceDifferences(neutral,liveModel.Pixels,liveModel.Frame.PixelWidth);
                            if (expected==PortraitConversationState.Thinking) thinkingMotion=Math.Max(thinkingMotion,faceChange);
                            else speakingMotion=Math.Max(speakingMotion,faceChange);
                        }
                        if (frames!=null)
                            WritePortraitPng(Path.Combine(frames,"frame-"+i.ToString("000",CultureInfo.InvariantCulture)+".png"));
                    }
                }
                if (thinkingMotion<30 || speakingMotion<30 || observedMouths.Count!=3)
                    throw new Exception("Missing visible conversation or speech phases: "+outfit);

                // A new accepted message can also be interrupted with no reply or audio.
                response=new TaskCompletionSource<HttpResponseMessage>();
                Task interrupted=chat.SubmitAsync("再说一句。");
                double end=start+ConversationFrames/(double)ConversationFps;
                AnimateAt(end,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.Thinking || interrupted.IsCompleted)
                    throw new Exception("Second chat turn did not arm thinking.");
                chat.PrepareVoiceInput();
                AwaitConversationSmoke(interrupted);
                AnimateAt(end+.05,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.None)
                    throw new Exception("Interrupted chat retained conversation motion: "+outfit);

                // The mock transport completes without advancing even one animation frame.
                // A successful very fast reply still deserves a perceptible acknowledgement.
                response=new TaskCompletionSource<HttpResponseMessage>();
                double quickStart=elapsed.Elapsed.TotalSeconds;
                Task quick=chat.SubmitAsync("窗外有云。");
                response.SetResult(ConversationSmokeReply());
                AwaitConversationSmoke(quick);
                AnimateAt(quickStart+.07,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.Thinking)
                    throw new Exception("Immediate text reply lost its brief thinking pose: "+outfit);
                AnimateAt(quickStart+.68,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.None)
                    throw new Exception("Immediate text reply retained the thinking pose: "+outfit);

                response=new TaskCompletionSource<HttpResponseMessage>();
                double failedAt=elapsed.Elapsed.TotalSeconds;
                Task failure=chat.SubmitAsync("再聊几句。");
                response.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) {
                    Content=new StringContent("{}",Encoding.UTF8,"application/json")
                });
                AwaitConversationSmoke(failure);
                AnimateAt(failedAt+.07,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.None)
                    throw new Exception("A failed turn inherited the fast-reply acknowledgement: "+outfit);
                response=new TaskCompletionSource<HttpResponseMessage>();
                double canceledAt=elapsed.Elapsed.TotalSeconds;
                Task canceled=chat.SubmitAsync("下一句话。");
                chat.PrepareVoiceInput();
                AwaitConversationSmoke(canceled);
                AnimateAt(canceledAt+.07,new Vector(),false,silent);
                if (portraitConversation!=PortraitConversationState.None)
                    throw new Exception("An interrupted turn showed a stale thinking pose: "+outfit);
            }
            finally
            {
                chat.Close();
                response.TrySetCanceled();
                chatWindow=null;
                thinking=false;
                bubble.Visibility=Visibility.Collapsed;
            }
        }

        private static int ConversationFaceDifferences(byte[] a,byte[] b,int width)
        {
            int count=0;
            int y0=Live2DPetModel.OverheadPadding+(int)(.10*Live2DPetModel.PortraitHeight);
            int y1=Live2DPetModel.OverheadPadding+(int)(.28*Live2DPetModel.PortraitHeight);
            for(int y=y0;y<y1;y++)
                for(int x=width*39/100;x<width*61/100;x++)
                {
                    int p=(y*width+x)*4;
                    if(Math.Abs(a[p]-b[p])>5 || Math.Abs(a[p+1]-b[p+1])>5 || Math.Abs(a[p+2]-b[p+2])>5) count++;
                }
            return count;
        }
    }
}
