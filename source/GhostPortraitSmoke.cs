using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AnnaDesktopPet
{
    internal sealed partial class PetWindow
    {
        // Use the same selector, save handler and outfit-change event as the tray menu.
        private static void SelectSmokeOutfit(string outfit)
        {
            Appearance.Show();
            Window picker = null;
            foreach (Window open in Application.Current.Windows)
                if (LogicalTreeHelper.FindLogicalNode(open, "OutfitSelector") is ComboBox) { picker = open; break; }
            if (picker == null) throw new Exception("Outfit selector did not open.");
            try
            {
                var clothes = (ComboBox)LogicalTreeHelper.FindLogicalNode(picker, "OutfitSelector");
                if (clothes.Items.Count != 3 || !clothes.Items.Contains(Appearance.Uniform1Portrait) ||
                    !clothes.Items.Contains(Appearance.Uniform2Portrait) || !clothes.Items.Contains(Appearance.GhostPortrait))
                    throw new Exception("The outfit selector must contain exactly the three current portraits.");
                if (!clothes.Items.Contains(outfit))
                    throw new Exception("Missing outfit in selector: " + outfit);
                clothes.SelectedItem = outfit;
                GptUi.FindSmokeButton(picker, "保存设置").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (Appearance.Outfit != outfit || picker.IsVisible) throw new Exception("Outfit selection was not applied.");
            }
            finally { if (picker.IsVisible) picker.Close(); }
        }

        private string VerifyGhostOutfitSmoke()
        {
            string original = Appearance.Outfit;
            bool enabled = mediaSettings.MotionsEnabled;
            SelectSmokeOutfit(Appearance.GhostPortrait);
            try
            {
                if (liveModel == null || !liveModel.HasIndependentFlames) throw new Exception("Ghost Live2D model or flame parameters missing.");
                mediaSettings.MotionsEnabled = true;
                UpdateLayout();
                foreach (string pose in new[]{"idle","wave","happy","walk","think","eat","speak","rest","sleep"})
                {
                    PlayMotion(pose,2);
                    AnimateAt(poseStarted+.02);
                    AnimateAt(poseStarted+.48);
                    if (activePose != (pose == "rest" ? "sleep" : pose) || character.Source != liveModel.Frame ||
                        !Object.ReferenceEquals(alphaPixels,liveModel.Pixels))
                        throw new Exception("A ghost pose fell back to a sprite: "+pose);
                }
                VerifyIdleGestureFrames();
                VerifyGhostFlames();
                // Pick a solid flame interior, a solid torso pixel, and a transparent
                // corner on the displayed surface, including WPF's fit and transforms.
                bubble.Visibility=Visibility.Collapsed;
                CheckPortraitHit(.32,.08,.05,.04,true); CheckPortraitHit(.68,.08,.05,.04,true);
                CheckPortraitHit(.5,.50,.08,.10,true); CheckPortraitHit(.02,.02,.01,.01,false);
                if (Program.GhostPreviewDirectory != null) ExportGhostFrames(Program.GhostPreviewDirectory);
                VerifyGhostActions(Program.GhostActionsPreviewDirectory);
            }
            finally
            {
                poseUntil=0; mediaSettings.MotionsEnabled=enabled;
                SelectSmokeOutfit(original);
            }
            if (UsesDefaultUniform && (liveModel == null || liveModel.HasIndependentFlames))
                throw new Exception("Switching back to the uniform did not restore its model.");
            return "Ghost portrait: real outfit switch, every pose, independent animated flames, body/flame alpha hit testing, action gaze lock and return to uniform verified\n" +
                "Seven ghost actions verified: timed performance, click, return to idle, independent flames, cursor lock and cancel on outfit switch";
        }

        private static readonly GhostGesture[] GhostShowcase = {
            GhostGesture.StartledByFlame, GhostGesture.DrowsyFlame, GhostGesture.RollCall,
            GhostGesture.Breeze, GhostGesture.FloatUp, GhostGesture.SleepyPeek, GhostGesture.PlayScare
        };

        private void VerifyGhostActions(string previewDirectory)
        {
            poseUntil = happyUntil = 0;
            Pet();
            if (ghostGestures.Current != GhostGesture.PlayScare)
                throw new Exception("Petting the ghost did not start her playful scare.");
            SelectSmokeOutfit(Appearance.Uniform1Portrait);
            if (ghostGestures.Current != GhostGesture.None)
                throw new Exception("The ghost action remained active on another skin.");
            SelectSmokeOutfit(Appearance.GhostPortrait);
            bubble.Visibility = Visibility.Collapsed;
            if (ghostGestures.Current != GhostGesture.None)
                throw new Exception("Changing back restarted an old ghost action.");

            const int fps = 20, leadFrames = 6;
            double blockStart = Math.Ceiling(elapsed.Elapsed.TotalSeconds) + 4;
            double longest = 0;
            foreach(GhostGesture kind in GhostShowcase) longest=Math.Max(longest,GhostGestures.Duration(kind));
            var timing = new System.Text.StringBuilder();
            foreach (GhostGesture kind in GhostShowcase)
            {
                string dir = previewDirectory == null ? null : Path.Combine(previewDirectory, kind.ToString());
                if (dir != null) Directory.CreateDirectory(dir);
                double duration = GhostGestures.Duration(kind);
                int clipFrames = leadFrames + (int)Math.Ceiling((duration + .55)*fps) + 1;
                int frames = dir == null ? clipFrames : leadFrames + (int)Math.Ceiling((longest + .55)*fps) + 1;
                timing.AppendLine(String.Join("\t",kind.ToString(),clipFrames.ToString(CultureInfo.InvariantCulture),
                    frames.ToString(CultureInfo.InvariantCulture),fps.ToString(CultureInfo.InvariantCulture)));
                int step = (dir == null ? 5 : 1);
                for (int index = 0; index < frames; index++)
                {
                    if (index % step != 0 && index != leadFrames && index != frames-1) continue;
                    double now = blockStart + index/(double)fps;
                    if (index == leadFrames && !TryStartGhostGesture(kind, now))
                        throw new Exception("Cannot start ghost action: "+kind);
                    AnimateAt(now, new Vector(0,0));
                    if (character.Source != liveModel.Frame || !Object.ReferenceEquals(alphaPixels,liveModel.Pixels))
                        throw new Exception("Ghost action lost the model or hit cache: "+kind);
                    double actionTime = (index-leadFrames)/(double)fps;
                    if (actionTime>.01 && actionTime<duration-.01 &&
                        (ghostGestures.Current!=kind || !ghostGestures.Pose.Face.ControlsGaze))
                        throw new Exception("A ghost action stopped early or lost control of gaze: "+kind);
                    if (dir!=null)
                        WritePortraitPng(Path.Combine(dir,"frame-"+index.ToString("000",CultureInfo.InvariantCulture)+".png"));
                }
                if (ghostGestures.Current != GhostGesture.None || ghostGestures.Pose.Face.ControlsGaze)
                    throw new Exception("Ghost action did not return to free idle: "+kind);
                VerifyGhostActionPixels(kind);
                blockStart += frames/(double)fps + 1;
            }
            if (previewDirectory != null) File.WriteAllText(Path.Combine(previewDirectory,"timing.tsv"),timing.ToString());
            VerifyAutonomousGhostWindow(blockStart + 2);
        }

        private void VerifyAutonomousGhostWindow(double start)
        {
            bool wasEnabled = mediaSettings.AutonomousEnabled;
            try
            {
                foreach (bool enabled in new[]{true,false})
                {
                    ghostGestures.Reset(); idleGestures.Reset();
                    mediaSettings.AutonomousEnabled = enabled;
                    bool observed = false;
                    for (int i=0;i<=70;i++)
                    {
                        AnimateAt(start + i*.5,new Vector(),true);
                        if (ghostGestures.Current == GhostGesture.PlayScare)
                            throw new Exception("The click-only ghost scare fired on its own.");
                        observed |= ghostGestures.Current != GhostGesture.None;
                    }
                    if (observed != enabled)
                        throw new Exception("Ghost idle actions did not respect the autonomous setting: "+enabled);
                    start += 36;
                }
            }
            finally
            {
                mediaSettings.AutonomousEnabled=wasEnabled;
                ghostGestures.Reset(); idleGestures.Reset();
            }
        }

        private void VerifyGhostActionPixels(GhostGesture kind)
        {
            // Every geometry comparison uses equal clocks and identical starting eye
            // states, so the ongoing flame loops and spontaneous blinks cancel out.
            var a = new LiveFaceTracking(new Random(733));
            var b = new LiveFaceTracking(new Random(733));
            var free = new LiveFaceTracking(new Random(733));
            int changedPixels = 0;
            double duration = GhostGestures.Duration(kind);
            for (int frame=0; frame/12.0 < duration; frame++)
            {
                double t = frame/12.0;
                GhostGesturePose action = GhostGestures.Sample(kind,t);
                liveModel.RenderTracked(a,t,new Vector(-1,.9),action.Face,0,true,action);
                byte[] authored = (byte[])liveModel.Pixels.Clone();
                liveModel.RenderTracked(b,t,new Vector(1,-.9),action.Face,0,true,action);
                if (PixelDifferences(authored, liveModel.Pixels, 0) != 0)
                    throw new Exception("Opposite mouse positions changed a ghost performance: "+kind);
                liveModel.RenderTracked(free,t,new Vector(),new IdleGesturePose(),0,true);
                changedPixels = Math.Max(changedPixels,PixelDifferences(authored,liveModel.Pixels,4));
            }
            if (changedPixels < 50) throw new Exception("Ghost action has no discernible performance: "+kind);
        }

        private static int PixelDifferences(byte[] first, byte[] second, int tolerance)
        {
            int changed=0;
            for(int i=0;i<first.Length;i+=4)
                if (Math.Abs(first[i]-second[i])>tolerance || Math.Abs(first[i+1]-second[i+1])>tolerance ||
                    Math.Abs(first[i+2]-second[i+2])>tolerance || Math.Abs(first[i+3]-second[i+3])>tolerance)
                    changed++;
            return changed;
        }

        private void CheckPortraitHit(double centerX,double centerY,double rx,double ry,bool solid)
        {
            int height = Live2DPetModel.PortraitHeight, top = Live2DPetModel.OverheadPadding;
            double scale = Math.Min(character.ActualWidth/pixelWidth,character.ActualHeight/pixelHeight);
            for (int y=top+(int)((centerY-ry)*height);y<top+(centerY+ry)*height;y+=2)
                for (int x=(int)((centerX-rx)*pixelWidth);x<(centerX+rx)*pixelWidth;x+=2)
                {
                    int i=(y*pixelWidth+x)*4+3;
                    if (solid ? alphaPixels[i] < 180 : alphaPixels[i] != 0) continue;
                    var point = new Point((character.ActualWidth-pixelWidth*scale)*.5+(x+.5)*scale,
                        (character.ActualHeight-pixelHeight*scale)*.5+(y+.5)*scale);
                    if (IsOpaqueAt(character.PointToScreen(point)) == solid) return;
                }
            throw new Exception("Portrait alpha hit failed in region "+centerX+", "+centerY);
        }

        private void VerifyGhostFlames()
        {
            var pose=new IdleGesturePose{ControlsGaze=true};
            var neutral=new GhostFlamePose();
            liveModel.Render(0,0,0,1,0,pose,neutral);
            byte[] reference=(byte[])liveModel.Pixels.Clone();
            foreach(bool left in new[]{true,false})
            {
                var single=left?new GhostFlamePose{Left=1}:new GhostFlamePose{Right=1};
                liveModel.Render(0,0,0,1,0,pose,single);
                int width=liveModel.Frame.PixelWidth,changes=0;
                for(int i=0;i<reference.Length;i+=4)
                {
                    if(reference[i]==liveModel.Pixels[i]&&reference[i+1]==liveModel.Pixels[i+1]&&
                       reference[i+2]==liveModel.Pixels[i+2]&&reference[i+3]==liveModel.Pixels[i+3])continue;
                    double x=(i/4%width+.5)/width, y=(i/4/width-Live2DPetModel.OverheadPadding+.5)/Live2DPetModel.PortraitHeight;
                    if (y>.145 || (left?(x<.23||x>.43):(x<.57||x>.77)))
                        throw new Exception("Ghost flame deformed the character or the opposite flame.");
                    changes++;
                }
                if (changes<30) throw new Exception("Ghost flame did not move independently.");
            }
            var locked = new LiveFaceTracking(new Random(1));
            liveModel.RenderTracked(locked,0,new Vector(-1,-1),pose,0,true);
            reference=(byte[])liveModel.Pixels.Clone();
            liveModel.RenderTracked(locked,.75,new Vector(1,1),pose,0,true);
            int leftDelta=0,rightDelta=0;
            for(int i=0;i<reference.Length;i+=4)
            {
                if(reference[i]==liveModel.Pixels[i] && reference[i+2]==liveModel.Pixels[i+2] && reference[i+3]==liveModel.Pixels[i+3])continue;
                if (i/4/liveModel.Frame.PixelWidth>Live2DPetModel.OverheadPadding+.145*Live2DPetModel.PortraitHeight)
                    throw new Exception("Locked ghost face reacted to mouse movement.");
                if(i/4%liveModel.Frame.PixelWidth < liveModel.Frame.PixelWidth/2)leftDelta++;else rightDelta++;
            }
            if(leftDelta<30||rightDelta<30)throw new Exception("Both ghost flames must move while gaze is locked.");
        }

        private void ExportGhostFrames(string directory)
        {
            Directory.CreateDirectory(directory);
            var still=new IdleGesturePose{ControlsGaze=true};
            WriteGhostSample(directory,"neutral",0,still,new Vector(),0);
            WriteGhostSample(directory,"flames-a",.10,still,new Vector(),0);
            WriteGhostSample(directory,"flames-b",1.2,still,new Vector(),0);
            WriteGhostSample(directory,"look-left",0,new IdleGesturePose(),new Vector(-.94,.08),0);
            WriteGhostSample(directory,"look-right",0,new IdleGesturePose(),new Vector(.94,.08),0);
            WriteGhostSample(directory,"blink",0,new IdleGesturePose{ControlsGaze=true,EyeClosure=1},new Vector(),0);
            WriteGhostSample(directory,"speak",0,still,new Vector(),.9);
            WriteGhostSample(directory,"listen",0,IdleGestures.Sample(IdleGesture.Listen,1.22,1),new Vector(),0);
            var face=new LiveFaceTracking(new Random(22));
            string sequence=Path.Combine(directory,"sequence"); Directory.CreateDirectory(sequence);
            for(int i=0;i<288;i++)
            {
                double t=i/24.0;
                IdleGesturePose reaction = IdleGestures.Sample(IdleGesture.Listen,t-4,1);
                double look = Math.Max(0,Math.Min(1,Math.Min(t-.55,10.4-t)));
                var pointer = new Vector(-.47*Math.Sin(t*Math.PI/3)*look, .2*Math.Sin(t*Math.PI/4)*look);
                liveModel.RenderTracked(face,t,pointer,reaction,0,true);
                WritePortraitPng(Path.Combine(sequence,"frame-"+i.ToString("000",CultureInfo.InvariantCulture)+".png"));
            }
        }

        private void WriteGhostSample(string dir,string name,double time,IdleGesturePose pose,Vector target,double mouth)
        {
            var face=new LiveFaceTracking(new Random(22));
            time+=2;
            for(int i=0;i<=24;i++)face.Update(time-1+i/24.0,target.X,target.Y,pose);
            liveModel.RenderTracked(face,time,target,pose,mouth,true);
            WritePortraitPng(Path.Combine(dir,name+".png"));
        }

        private void WritePortraitPng(string path)
        {
            var png=new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(liveModel.Frame));
            using(var output=File.Create(path))png.Save(output);
        }
    }
}
