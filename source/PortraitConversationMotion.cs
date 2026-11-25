using System;

namespace AnnaDesktopPet
{
    internal enum PortraitConversationState { None, Thinking, Speaking }
    internal enum ConversationPortrait { Uniform1, Uniform2, Ghost }

    internal sealed class PortraitConversationMotion
    {
        private PortraitConversationState state;
        private GhostGesturePose from, pose;
        private double started, lastTime, voiceEnergy;
        private bool active;

        internal void Reset()
        {
            active = false;
            state = PortraitConversationState.None;
            from = pose = new GhostGesturePose();
            voiceEnergy = 0;
        }

        private static double Ease(double seconds, double duration)
        {
            double x = Math.Min(1, Math.Max(0, seconds/duration));
            return x*x*(3-2*x);
        }

        // A single rise, hold and return, with motionless endpoints so it loops cleanly.
        private static double Beat(double t, double start, double peak, double release, double end)
        {
            return Ease(t-start,peak-start)-Ease(t-release,end-release);
        }

        private static GhostGesturePose Think(ConversationPortrait skin, double t)
        {
            var shot = new GhostGesturePose();
            double p;
            switch (skin)
            {
                case ConversationPortrait.Uniform2:
                    // The chin dips but the eyes rise behind the glasses. Briefly look
                    // back to the viewer before returning to the thought.
                    p=t%5.9;
                    double reconsider=Beat(p,2.5,2.95,3.45,4.55);
                    shot.Face.HeadPitch=.47-.35*reconsider;
                    shot.Face.ListenTilt=.40-.28*reconsider;
                    shot.Face.EyeLook=.52-.64*reconsider;
                    shot.Face.DownLook=-.64+.52*reconsider;
                    break;
                case ConversationPortrait.Ghost:
                    // Look between the will-o'-wisps. Their regular floating phases
                    // continue while the nearer flame leans in by a few source pixels.
                    p=t%7.8;
                    double across=Beat(p,2.6,3.45,5.0,6.3);
                    double follow=Beat(p,2.84,3.85,5.16,6.7);
                    shot.Face.EyeLook=-.78+1.49*across;
                    shot.Face.DownLook=-.38-.13*across;
                    shot.Face.ListenTilt=-.52+.94*follow;
                    shot.Face.HeadPitch=-.16+.17*Math.Cos(2*Math.PI*p/7.8);
                    shot.FlameLeftX=5*(1-follow); shot.FlameRightX=-5*follow;
                    break;
                default:
                    // First her eyes go to one side; a little later she inclines her head.
                    p=t%6.5;
                    double glance=Beat(p,.12,.56,3.10,4.25);
                    double tilt=Beat(p,.33,1.04,3.35,4.72);
                    shot.Face.EyeLook=-.25-.53*glance;
                    shot.Face.DownLook=-.23-.48*glance;
                    shot.Face.ListenTilt=-.22-.34*tilt;
                    shot.Face.HeadPitch=.18+.22*tilt;
                    break;
            }
            return shot;
        }

        private static GhostGesturePose Speak(ConversationPortrait skin, double t, double energy)
        {
            var shot = new GhostGesturePose();
            switch (skin)
            {
                case ConversationPortrait.Uniform2:
                    // A small affirmative nod on a phrase, followed by a lighter accent.
                    double beat=t%3.5;
                    double nod=Beat(beat,.16,.68,.83,1.33)+.45*Beat(beat,2.02,2.37,2.46,2.92);
                    shot.Face.HeadPitch=-.18+(.31+.38*energy)*nod;
                    shot.Face.DownLook=-.15+.36*nod;
                    shot.Face.ListenTilt=.09*Math.Sin(2*Math.PI*beat/3.5);
                    shot.Face.SnackSmile=.08+.09*energy;
                    break;
                case ConversationPortrait.Ghost:
                    // Her head inclines to the listener as the flame tips lift on speech.
                    double drift=Math.Sin(2*Math.PI*t/4.8);
                    shot.Face.ListenTilt=.18+.30*drift;
                    shot.Face.HeadPitch=-.13-.22*energy+.11*drift;
                    shot.Face.EyeLook=.12*drift;
                    shot.Face.SnackSmile=.10+.12*energy;
                    shot.FlameLeftY=-5*energy; shot.FlameRightY=-3.8*energy;
                    shot.FlameLeftTip=.13*energy; shot.FlameRightTip=-.11*energy;
                    break;
                default:
                    // Lift her chin to address the listener, then settle on a phrase ending.
                    double finish=Beat(t%3.9,1.00,1.55,1.80,2.48);
                    shot.Face.HeadPitch=-.33-.25*energy+.48*finish;
                    shot.Face.ListenTilt=-.20+.30*finish;
                    shot.Face.DownLook=-.26+.35*finish;
                    shot.Face.SnackSmile=.14+.14*energy;
                    break;
            }
            return shot;
        }

        internal bool Update(ConversationPortrait skin, PortraitConversationState requested,
                             double now, double mouth, GhostGesturePose previousFrame,
                             Uniform2GesturePose nod, out GhostGesturePose result)
        {
            if (active && now<lastTime) Reset();
            if (!active && requested==PortraitConversationState.None)
            {
                result = new GhostGesturePose();
                return false;
            }
            double dt=active ? Math.Min(.25,now-lastTime) : 0;
            lastTime=now;
            if (!active || requested!=state)
            {
                from=active ? pose : previousFrame;
                from.MouthOpen=0;
                started=now;
                state=requested;
                active=true;
            }
            double t=now-started;
            double duration=state==PortraitConversationState.None ? .23 : state==PortraitConversationState.Speaking ? .26 : .38;
            if (state==PortraitConversationState.None && t>=duration)
            {
                Reset();
                result=new GhostGesturePose();
                return false;
            }
            double loudness=state==PortraitConversationState.Speaking ? Math.Max(0,Math.Min(1,mouth)) : 0;
            double tau=loudness>voiceEnergy ? .13 : .27;
            voiceEnergy+=(loudness-voiceEnergy)*(1-Math.Exp(-dt/tau));
            var target = new GhostGesturePose();
            if (state==PortraitConversationState.Thinking)
            {
                target=Think(skin,t);
                if (skin==ConversationPortrait.Uniform2 && nod.Face.ControlsGaze)
                {
                    target.Face.HeadPitch+=.82*nod.Face.HeadPitch;
                    target.Face.DownLook+=.37*nod.Face.DownLook;
                    target.Face.EyeClosure=nod.Face.EyeClosure;
                }
            }
            else if (state==PortraitConversationState.Speaking) target=Speak(skin,t,voiceEnergy);
            pose=GhostGesturePose.Blend(from,target,Ease(t,duration));
            pose.Face.ControlsGaze=true;
            result=pose;
            return true;
        }
    }
}
