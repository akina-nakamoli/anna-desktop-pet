using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace AnnaDesktopPet
{
    internal sealed partial class PetWindow
    {
        private string VerifyUniform2OutfitSmoke()
        {
            string original = Appearance.Outfit;
            bool enabled = mediaSettings.MotionsEnabled;
            try
            {
                if (Appearance.NormalizeOutfit("默认") != Appearance.Uniform1Portrait ||
                    Appearance.NormalizeOutfit("校服2") != Appearance.Uniform2Portrait ||
                    Appearance.NormalizeOutfit("幽灵服") != Appearance.GhostPortrait)
                    throw new Exception("A saved legacy outfit name no longer resolves to its current portrait.");
                SelectSmokeOutfit(Appearance.Uniform1Portrait);
                if (liveModel == null || liveModel.HasGlasses || liveModel.HasIndependentFlames)
                    throw new Exception("School uniform 1 was not restored before switching portraits.");
                SelectSmokeOutfit(Appearance.Uniform2Portrait);
                if (liveModel == null || !liveModel.HasGlasses || !liveModel.HasPortraitRegistration ||
                    liveModel.HasIndependentFlames || !Appearance.IsStandingPortrait || Appearance.IsGhostPortrait)
                    throw new Exception("School uniform 2 portrait, glasses or facial registration missing.");
                mediaSettings.MotionsEnabled = true;
                if (TryStartGhostGesture(GhostGesture.PlayScare, elapsed.Elapsed.TotalSeconds))
                    throw new Exception("A ghost action was available on the school uniform.");
                UpdateLayout();
                foreach (string pose in new[]{"idle","wave","happy","walk","think","eat","speak","rest","sleep"})
                {
                    PlayMotion(pose,2);
                    AnimateAt(poseStarted+.02);
                    AnimateAt(poseStarted+.48);
                    if (activePose != (pose == "rest" ? "sleep" : pose) || character.Source != liveModel.Frame ||
                        !Object.ReferenceEquals(alphaPixels,liveModel.Pixels))
                        throw new Exception("School uniform 2 fell back to a sprite: "+pose);
                }
                VerifyIdleGestureFrames();
                bubble.Visibility = Visibility.Collapsed;
                CheckPortraitHit(.5,.50,.06,.07,true);
                CheckPortraitHit(.025,.025,.02,.02,false);
                VerifyUniform2Face(Program.Uniform2PreviewDirectory);
                if (Program.Uniform2PreviewDirectory != null) ExportUniform2Sequence(Program.Uniform2PreviewDirectory);
                VerifyUniform2Actions(Program.Uniform2ActionsPreviewDirectory);
                if (!TryStartUniform2Nod(elapsed.Elapsed.TotalSeconds))
                    throw new Exception("Could not stage an active nod before changing the outfit.");
                SelectSmokeOutfit(Appearance.GhostPortrait);
                if (liveModel == null || !liveModel.HasIndependentFlames || liveModel.HasGlasses || uniform2Director.Current != Uniform2Gesture.None ||
                    TryStartUniform2Nod(elapsed.Elapsed.TotalSeconds))
                    throw new Exception("Changing to the ghost portrait did not release the school uniform actions.");
            }
            finally
            {
                poseUntil = 0;
                mediaSettings.MotionsEnabled = enabled;
                SelectSmokeOutfit(original);
            }
            return "Outfit selector: exactly three current portraits; saved legacy selections resolve to the matching current outfit\n" +
                   "Uniform2 portrait: real outfit switch and restoration, nine pose routes, five idle gestures, gaze lock, eyes, three mouth levels, glasses and alpha hit testing verified\n" +
                   "Uniform2 actions: peek, nod, drowse; real chat submission, interrupted idle, two-minute gate, fixed arms, gaze lock and neutral return verified";
        }

        private void VerifyUniform2Actions(string directory)
        {
            bool autonomousEnabled = mediaSettings.AutonomousEnabled;
            poseUntil = happyUntil = 0;
            bubble.Visibility = Visibility.Collapsed;
            idleGestures.Reset();
            uniform2Director.Reset();
            try
            {
                double time = Math.Ceiling(elapsed.Elapsed.TotalSeconds) + 1;
                mediaSettings.AutonomousEnabled = false;
                for (int i=0; i<260; i++, time+=.5)
                {
                    AnimateAt(time,new Vector(),true);
                    if (uniform2Director.Current != Uniform2Gesture.None)
                        throw new Exception("Automatic school uniform actions started while disabled.");
                }
                mediaSettings.AutonomousEnabled = true;
                using (var reference=Live2DPetModel.Load(Appearance.ModelManifest))
                {
                    double quietStart = time;
                    double peek = WaitForUniform2Action(Uniform2Gesture.Peek,time,quietStart,90);
                    VerifyUniform2Take(Uniform2Gesture.Peek,"A",peek,reference,directory);
                    time = peek + Uniform2Gestures.Duration(Uniform2Gesture.Peek) + .04;
                    // Record an actual pointer press/release boundary through the window's
                    // idle eligibility. Time before the interaction cannot count for drowsing.
                    pressed = true;
                    try { AnimateAt(time,new Vector(),true); }
                    finally { pressed=false; }
                    quietStart = time+.25;
                    double drowse = WaitForUniform2Action(Uniform2Gesture.Drowse,quietStart,quietStart,240);
                    VerifyUniform2Take(Uniform2Gesture.Drowse,"C",drowse,reference,directory);
                    time = drowse + Uniform2Gestures.Duration(Uniform2Gesture.Drowse) + .10;
                    AnimateAt(time,new Vector(),true);
                    VerifyUniform2ChatNod(time+.25,reference,directory);
                }
            }
            finally
            {
                mediaSettings.AutonomousEnabled = autonomousEnabled;
                uniform2Director.Reset();
                idleGestures.Reset();
                liveFace.Reset();
                lastLiveFrame = -1;
            }
        }

        private double WaitForUniform2Action(Uniform2Gesture expected, double start, double quietStart, double limit)
        {
            for (double now=start; now<start+limit; now+=.5)
            {
                AnimateAt(now,new Vector(),true);
                double earliestDrowse = quietStart + Uniform2GestureDirector.DrowseAfterSeconds;
                if (uniform2Director.Current == Uniform2Gesture.Drowse && now < earliestDrowse-1e-8)
                    throw new Exception(String.Format(CultureInfo.InvariantCulture,
                        "School uniform 2 fell asleep early while waiting for {0}: at {1:G17}, quiet since {2:G17}, due {3:G17}.",
                        expected,now,quietStart,earliestDrowse));
                if (uniform2Director.Current == expected) return now;
            }
            throw new Exception("School uniform 2 did not autonomously start "+expected);
        }

        private void VerifyUniform2ChatNod(double now, Live2DPetModel reference, string directory)
        {
            // Hold a real, accepted chat turn in the thinking state. The transport stays
            // entirely in-process; closing the test chat cancels the held task.
            var waiting = new TaskCompletionSource<HttpResponseMessage>();
            var client = new GptClient(delegate(HttpRequestMessage req, System.Threading.CancellationToken token) {
                token.Register(delegate { waiting.TrySetCanceled(); });
                return waiting.Task;
            });
            var chat = new GptChatWindow(this,new List<ChatMessage>(),new GptSettings { ApiKey="offline-action-test" },
                Say,delegate(bool busy){ thinking=busy; },null,null,delegate { return false; },null,null,null,client);
            chatWindow = chat;
            try
            {
                int submitted=0;
                chat.UserMessageSubmitted += delegate { submitted++; TryStartUniform2Nod(now); };
                chat.Show();
                chat.SubmitAsync("   ");
                if (submitted!=0) throw new Exception("A blank message triggered the nod.");
                Task local=chat.SubmitAsync("查看记忆");
                if (!local.IsCompleted || submitted!=0)
                    throw new Exception("A local memory command triggered a conversational nod.");
                Task pending=chat.SubmitAsync("今天的晚霞很好看。");
                if (pending.IsCompleted || !thinking || submitted!=1 || uniform2Director.Current!=Uniform2Gesture.Nod ||
                    TryStartUniform2Nod(now+.1))
                    throw new Exception("Submitting a normal message did not start exactly one school uniform nod.");
                VerifyUniform2Take(Uniform2Gesture.Nod,"B",now,reference,directory);
            }
            finally
            {
                chat.Close();
                waiting.TrySetCanceled();
                chatWindow=null;
                thinking=false;
                bubble.Visibility=Visibility.Collapsed;
            }
        }

        private void VerifyUniform2Take(Uniform2Gesture kind,string label,double start,Live2DPetModel reference,string output)
        {
            string frames = output==null ? null : Path.Combine(output,label,"sequence");
            if (frames!=null) Directory.CreateDirectory(frames);
            liveFace.Reset();
            lastLiveFrame=-1;
            // A real idle blink may have just finished within the software renderer's
            // subpixel cache tolerance. Give both models the same last raster before
            // comparing a neutral-to-neutral take, independently of random idle timing.
            liveModel.Render(0,0,0,0,1);
            reference.Render(0,0,0,0,1);
            liveModel.Render(0,0,0,1,0);
            reference.Render(0,0,0,1,0);
            byte[] neutral=(byte[])reference.Pixels.Clone();
            if (PixelDifferences(neutral,liveModel.Pixels,0)!=0)
                throw new Exception("Uniform2 models did not produce identical neutral reference pixels: "+kind);
            int maximumChange=0;
            for (int frame=0; frame<96; frame++)
            {
                if (frames==null && frame%6!=0 && frame!=95) continue;
                double t=frame/24.0;
                var pointer=new Vector(Math.Sin(t*4.9),Math.Cos(t*3.7));
                AnimateAt(start+t,pointer,true);
                if (uniform2Director.Current!=kind || !uniform2Director.Pose.Face.ControlsGaze ||
                    character.Source!=liveModel.Frame || !Object.ReferenceEquals(alphaPixels,liveModel.Pixels))
                    throw new Exception("The displayed uniform action lost its model or gaze lock: "+kind+" "+frame);
                Uniform2GesturePose shot=Uniform2Gestures.Sample(kind,t);
                reference.Render(shot.Turn,-pointer.X,-pointer.Y,1,0,shot.Face);
                int different=PixelDifferences(reference.Pixels,liveModel.Pixels,1);
                if (different!=0)
                    throw new Exception("Displayed action differs from its silent authored pose at the opposite cursor: "+kind+" "+frame+" ("+different+" pixels)");
                byte[] pixels=liveModel.Pixels;
                int lowerStart=(Live2DPetModel.OverheadPadding+(int)(Live2DPetModel.PortraitHeight*.475))*liveModel.Frame.PixelWidth*4;
                for (int j=lowerStart; j<pixels.Length; j++)
                    if (Math.Abs(pixels[j]-neutral[j])>2)
                        throw new Exception("The hands, skirt or legs moved in "+kind);
                int changed=PixelDifferences(pixels,neutral,3);
                if ((frame==0 || frame==95) && changed!=0)
                    throw new Exception("School uniform action did not begin/end on its neutral portrait: "+kind);
                maximumChange=Math.Max(maximumChange,changed);
                if (frames!=null)
                    WritePortraitPng(Path.Combine(frames,"frame-"+frame.ToString("000",CultureInfo.InvariantCulture)+".png"));
            }
            if (maximumChange<200) throw new Exception("School uniform action had insufficient visible movement: "+kind);
        }

        private void VerifyUniform2Face(string preview)
        {
            if (preview != null) Directory.CreateDirectory(preview);
            liveModel.Render(0,0,0,1,0);
            byte[] neutral = (byte[])liveModel.Pixels.Clone();
            if (preview != null) WritePortraitPng(Path.Combine(preview,"neutral.png"));
            foreach (double side in new[]{-1.0,1.0})
            {
                liveModel.Render(0,.9*side,0,1,0);
                if (PixelDifferences(neutral,liveModel.Pixels,3)<8) throw new Exception("School uniform 2 pupils did not follow.");
                liveModel.Render(.74*side,.9*side,0,1,0);
                if (preview != null) WritePortraitPng(Path.Combine(preview,side<0?"look-left.png":"look-right.png"));
            }
            liveModel.Render(0,0,0,0,0);
            if (PixelDifferences(neutral,liveModel.Pixels,3)<8) throw new Exception("School uniform 2 eyelids did not close.");
            if (preview != null) WritePortraitPng(Path.Combine(preview,"blink.png"));
            liveModel.Render(0,0,0,1,.5);
            byte[] half=(byte[])liveModel.Pixels.Clone();
            if (preview != null) WritePortraitPng(Path.Combine(preview,"speak-half.png"));
            liveModel.Render(0,0,0,1,1);
            if (PixelDifferences(neutral,half,3)<5 || PixelDifferences(half,liveModel.Pixels,3)<5 ||
                PixelDifferences(neutral,liveModel.Pixels,3)<5)
                throw new Exception("Closed, half-open and open school uniform 2 mouths were not distinct.");
            if (preview != null) WritePortraitPng(Path.Combine(preview,"speak.png"));
        }

        private void ExportUniform2Sequence(string directory)
        {
            string frames=Path.Combine(directory,"sequence");
            Directory.CreateDirectory(frames);
            var face=new LiveFaceTracking(new Random(22));
            string example=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reference","anna.wav");
            float[] voiceLevels=SpeechService.ReadMouthLevels(File.ReadAllBytes(example));
            if (voiceLevels==null || Array.IndexOf(voiceLevels,.5f)<0 || Array.IndexOf(voiceLevels,1f)<0)
                throw new Exception("The sample voice did not produce three lip-sync levels.");
            for (int i=0;i<144;i++)
            {
                double t=i/24.0;
                var target=new Vector();
                if (t<1.9) target.X=.90*Math.Sin(2*Math.PI*t/1.9);
                IdleGesturePose pose=IdleGestures.Sample(IdleGesture.Listen,t-2.95,1);
                double mouth=0;
                if (t>=1.9 && t<2.95)
                {
                    // Analyze a real bundled recording with the playback envelope
                    // and drive the native mouth without playing diagnostic audio.
                    int window=(int)((t-1.9)*50);
                    if (window<voiceLevels.Length) mouth=voiceLevels[window];
                    pose.ControlsGaze=true;
                }
                liveModel.RenderTracked(face,t,target,pose,mouth,true);
                WritePortraitPng(Path.Combine(frames,"frame-"+i.ToString("000",CultureInfo.InvariantCulture)+".png"));
            }
        }
    }
}
