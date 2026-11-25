using System;
using System.Collections.Generic;

namespace AnnaDesktopPet
{
    internal enum GhostGesture { None, StartledByFlame, DrowsyFlame, RollCall, Breeze, FloatUp, SleepyPeek, PlayScare }

    internal struct GhostGesturePose
    {
        internal IdleGesturePose Face;
        internal double MouthOpen, LeftEyeClosure, RightEyeClosure;
        // Offsets from the two freely floating lights, in original portrait pixels.
        internal double FlameLeftX, FlameLeftY, FlameRightX, FlameRightY;
        internal double FlameLeftTip, FlameRightTip, FlameLeftDim, FlameRightDim;
        internal double HairWind, HemWind, Lift, HemLag, Lean;

        internal static GhostGesturePose Blend(GhostGesturePose a, GhostGesturePose b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            double s = 1 - t;
            return new GhostGesturePose {
                Face=IdleGesturePose.Blend(a.Face,b.Face,t),
                MouthOpen=s*a.MouthOpen+t*b.MouthOpen,
                LeftEyeClosure=s*a.LeftEyeClosure+t*b.LeftEyeClosure, RightEyeClosure=s*a.RightEyeClosure+t*b.RightEyeClosure,
                FlameLeftX=s*a.FlameLeftX+t*b.FlameLeftX, FlameLeftY=s*a.FlameLeftY+t*b.FlameLeftY,
                FlameRightX=s*a.FlameRightX+t*b.FlameRightX, FlameRightY=s*a.FlameRightY+t*b.FlameRightY,
                FlameLeftTip=s*a.FlameLeftTip+t*b.FlameLeftTip, FlameRightTip=s*a.FlameRightTip+t*b.FlameRightTip,
                FlameLeftDim=s*a.FlameLeftDim+t*b.FlameLeftDim, FlameRightDim=s*a.FlameRightDim+t*b.FlameRightDim,
                HairWind=s*a.HairWind+t*b.HairWind, HemWind=s*a.HemWind+t*b.HemWind,
                Lift=s*a.Lift+t*b.Lift, HemLag=s*a.HemLag+t*b.HemLag, Lean=s*a.Lean+t*b.Lean
            };
        }
    }

    internal sealed class GhostGestures
    {
        private static readonly GhostGesture[] everyday = {
            GhostGesture.StartledByFlame, GhostGesture.DrowsyFlame, GhostGesture.RollCall, GhostGesture.Breeze, GhostGesture.FloatUp
        };
        internal const double SleepyIdleSeconds = 120;
        private readonly Random random;
        // Draw without replacement so all five everyday scenes get a turn.
        private readonly List<GhostGesture> remaining = new List<GhostGesture>();
        private GhostGesture lastChoice;
        private GhostGesturePose transitionFrom;
        private double started, lastInteraction = -1, next = -1, nextPeek, previousTick = -1, release = -1;
        private bool manual;

        internal GhostGesture Current { get; private set; }
        internal GhostGesturePose Pose { get; private set; }
        internal GhostGestures(Random random) { this.random = random; }

        internal static string Name(GhostGesture kind)
        {
            switch (kind)
            {
                case GhostGesture.StartledByFlame: return "被自己的鬼火吓一跳";
                case GhostGesture.DrowsyFlame: return "鬼火打瞌睡";
                case GhostGesture.RollCall: return "跟鬼火点名";
                case GhostGesture.Breeze: return "被风吹偏";
                case GhostGesture.FloatUp: return "偷偷飘高";
                case GhostGesture.SleepyPeek: return "困到只睁一只眼";
                case GhostGesture.PlayScare: return "假装吓你";
                default: return "";
            }
        }

        internal static double Duration(GhostGesture kind)
        {
            switch (kind)
            {
                case GhostGesture.StartledByFlame: return 4.25;
                case GhostGesture.DrowsyFlame: return 4.65;
                case GhostGesture.RollCall: return 4.35;
                case GhostGesture.Breeze: return 4.8;
                case GhostGesture.FloatUp: return 5.3;
                case GhostGesture.SleepyPeek: return 6.1;
                case GhostGesture.PlayScare: return 3.45;
                default: return 0;
            }
        }

        private static double Ease(double t, double start, double end)
        {
            if (t <= start) return 0;
            if (t >= end) return 1;
            double x = (t-start)/(end-start);
            return x*x*x*(10+x*(-15+6*x));
        }

        private static double Hold(double t, double start, double peak, double letGo, double rest)
        {
            return Ease(t,start,peak) - Ease(t,letGo,rest);
        }

        internal static GhostGesturePose Sample(GhostGesture kind, double t)
        {
            var p = new GhostGesturePose();
            if (!(t >= 0 && t < Duration(kind))) return p;
            p.Face.ControlsGaze = true;
            switch (kind)
            {
                case GhostGesture.StartledByFlame: {
                    double approach = Hold(t,0,1.37,1.77,2.62);
                    p.FlameLeftX = 64*approach - 6*Hold(t,2.28,2.64,2.69,3.12);
                    p.FlameLeftY = 95*Hold(t,.08,1.46,1.86,2.77);
                    p.FlameLeftTip = .75*Hold(t,.26,.78,1.43,1.94) - .52*Hold(t,1.94,2.32,2.46,2.95);
                    p.Face.EyeLook = -.81*Hold(t,.51,.96,1.91,2.60) - .56*Hold(t,2.76,3.08,3.50,4.14);
                    p.Face.DownLook = -.33*Hold(t,.59,1.03,1.92,2.47);
                    p.Face.EyeClosure = Hold(t,1.18,1.32,1.39,1.60);
                    p.Face.ListenTilt = .89*Hold(t,1.24,1.69,1.82,2.61) - .12*Hold(t,2.86,3.22,3.40,4.13);
                    p.Face.HeadPitch = -.45*Hold(t,1.26,1.62,1.70,2.34);
                    p.MouthOpen = .40*Hold(t,1.30,1.56,1.73,2.15);
                    break;
                }
                case GhostGesture.DrowsyFlame: {
                    double sink = Hold(t,0,2.10,2.36,2.92);
                    double rebound = Hold(t,2.58,2.99,3.08,3.74);
                    p.FlameRightY = 111*sink - 25*rebound;
                    p.FlameRightX = 11*sink - 5*rebound;
                    p.FlameRightTip = -.78*Hold(t,.24,1.96,2.36,2.84) + .59*rebound;
                    p.FlameRightDim = .42*sink - .28*rebound;
                    p.Face.EyeLook = .69*Hold(t,.40,1.09,3.49,4.45);
                    p.Face.DownLook = .62*Hold(t,.81,2.13,2.70,3.14) - .68*Hold(t,2.85,3.21,3.42,4.34);
                    p.Face.HeadPitch = .62*Hold(t,1.06,2.28,2.79,3.42) - .21*Hold(t,3.01,3.39,3.47,4.24);
                    p.Face.ListenTilt = -.20*Hold(t,.85,1.68,3.17,4.55);
                    break;
                }
                case GhostGesture.RollCall: {
                    double first = Hold(t,0,.31,.39,.76);
                    double second = Hold(t,.91,1.22,1.32,1.73);
                    double together = Hold(t,2.08,2.46,2.53,2.94);
                    p.FlameLeftY = -46*first - 42*together;
                    p.FlameRightY = -45*second - 41*together;
                    p.FlameLeftTip = -.66*first + .43*together;
                    p.FlameRightTip = .68*second - .40*together;
                    p.Face.EyeLook = -.92*Hold(t,.15,.41,.80,1.22) + .91*Hold(t,1.11,1.44,1.98,2.42);
                    p.Face.DownLook = -.46*Hold(t,.20,.50,2.60,3.12);
                    p.Face.ListenTilt = -.28*Hold(t,.31,.68,.91,1.38) + .30*Hold(t,1.29,1.69,2.13,2.66);
                    p.Face.EyeClosure = Hold(t,3.06,3.31,3.48,3.87);
                    break;
                }
                case GhostGesture.Breeze: {
                    double gust = Hold(t,0,.85,1.89,3.18);
                    double echo = Hold(t,2.88,3.40,3.57,4.36);
                    p.FlameLeftTip = .95*gust - .28*echo;
                    p.FlameRightTip = .88*Hold(t,.12,.97,2.07,3.37) - .23*Hold(t,3.09,3.57,3.69,4.48);
                    p.FlameLeftX = 23*gust - 4*echo;
                    p.FlameRightX = 20*Hold(t,.13,1.07,2.05,3.37);
                    p.HairWind = .95*Hold(t,.28,1.27,2.11,3.41) - .27*Hold(t,3.08,3.66,3.78,4.48);
                    p.HemWind = .96*Hold(t,.52,1.69,2.45,3.85) - .24*Hold(t,3.55,4.02,4.13,4.77);
                    p.Face.EyeLook = -.69*Hold(t,.46,.97,2.83,3.69);
                    p.Face.ListenTilt = -.48*Hold(t,.67,1.66,2.79,4.18);
                    p.Face.Daydream = .35*Hold(t,.70,1.40,1.91,2.84);
                    break;
                }
                case GhostGesture.FloatUp: {
                    p.Lift = Hold(t,0,1.84,2.73,4.78);
                    p.HemLag = .88*Hold(t,.13,.74,.90,1.95) - .79*Hold(t,2.91,3.64,3.87,4.97);
                    p.Face.DownLook = .94*Hold(t,1.12,1.74,2.98,4.20);
                    p.Face.HeadPitch = .70*Hold(t,1.40,2.11,3.01,4.67);
                    p.Face.SnackSmile = .32*Hold(t,2.13,2.74,3.35,4.62);
                    break;
                }
                case GhostGesture.SleepyPeek: {
                    double shut = Hold(t,.12,1.10,4.67,5.64);
                    double peek = Hold(t,2.29,2.75,3.10,3.68);
                    p.LeftEyeClosure = shut - .86*peek;
                    p.RightEyeClosure = shut;
                    p.Face.HeadPitch = .76*Hold(t,.21,1.57,4.68,6.02);
                    p.Face.ListenTilt = -.33*Hold(t,.42,1.62,4.69,5.95);
                    p.Face.EyeLook = -.87*Hold(t,2.16,2.61,3.27,3.81);
                    p.FlameLeftX = 42*Hold(t,1.40,2.57,3.47,4.55);
                    p.FlameLeftY = 90*Hold(t,1.45,2.62,3.59,4.68);
                    p.FlameLeftTip = .58*Hold(t,1.51,2.23,3.43,4.41);
                    break;
                }
                case GhostGesture.PlayScare: {
                    p.Face.EyeClosure = Hold(t,0,.47,.84,1.15);
                    double dim = Hold(t,.03,.59,.84,1.19);
                    double glint = Hold(t,.89,1.22,1.44,1.95);
                    p.FlameLeftDim = .89*dim - .85*glint;
                    p.FlameRightDim = .85*dim - .81*glint;
                    p.FlameLeftTip = -.50*glint;
                    p.FlameRightTip = .50*glint;
                    p.Lean = .93*Hold(t,.84,1.39,1.62,2.44);
                    p.MouthOpen = .84*Hold(t,.88,1.27,1.72,2.15);
                    p.Face.HeadPitch = -.30*Hold(t,.93,1.33,1.66,2.13);
                    p.Face.SnackSmile = .82*Hold(t,1.80,2.30,2.76,3.43);
                    p.Face.ListenTilt = .27*Hold(t,1.94,2.43,2.70,3.38);
                    break;
                }
            }
            return p;
        }

        internal void Reset()
        {
            Current = lastChoice = GhostGesture.None;
            Pose = transitionFrom = new GhostGesturePose();
            previousTick = next = release = lastInteraction = -1;
            nextPeek = 0;
            remaining.Clear();
            manual = false;
        }

        internal void NoteInteraction(double now)
        {
            lastInteraction = now;
            next = -1;
        }

        internal bool Trigger(GhostGesture kind, double now)
        {
            if (Duration(kind) == 0) return false;
            NoteInteraction(now);
            Start(kind, now, true);
            return true;
        }

        private void Start(GhostGesture kind, double now, bool byUser)
        {
            transitionFrom = Pose;
            started = previousTick = now;
            release = next = -1;
            manual = byUser;
            Current = kind;
            Pose = Sample(kind,0);
            if (transitionFrom.Face.ControlsGaze) Pose = transitionFrom;
        }

        internal void Interrupt(double now)
        {
            next = -1;
            if (Current == GhostGesture.None) return;
            transitionFrom = Pose;
            release = now;
            Current = GhostGesture.None;
            manual = false;
        }

        private GhostGesture Choose(double now)
        {
            if (now-lastInteraction >= SleepyIdleSeconds && now >= nextPeek) {
                nextPeek = now + 160 + random.NextDouble()*95;
                return GhostGesture.SleepyPeek;
            }
            if (remaining.Count == 0) remaining.AddRange(everyday);
            int index = random.Next(remaining.Count);
            if (remaining[index] == lastChoice && remaining.Count > 1)
                index = (index + 1 + random.Next(remaining.Count-1)) % remaining.Count;
            var chosen = remaining[index];
            remaining.RemoveAt(index);
            return chosen;
        }

        internal void Update(double now, bool eligible, bool autonomousEnabled)
        {
            if (previousTick >= 0 && (now < previousTick || now - previousTick > 1)) Reset();
            previousTick = now;
            // Only available idle time contributes to the late drowsy scene.
            // A long chat, open menu or disabled autonomous mode starts a new wait.
            if (lastInteraction < 0 || !eligible || !autonomousEnabled) lastInteraction = now;
            if (!eligible || (!manual && !autonomousEnabled)) Interrupt(now);
            if (release >= 0)
            {
                Pose = GhostGesturePose.Blend(transitionFrom,new GhostGesturePose(),Ease(now,release,release+.32));
                if (now < release+.32) return;
                release = -1;
            }
            if (Current != GhostGesture.None)
            {
                double t = now-started;
                var sampled = Sample(Current,t);
                Pose = transitionFrom.Face.ControlsGaze && t < .25
                    ? GhostGesturePose.Blend(transitionFrom,sampled,Ease(t,0,.25)) : sampled;
                if (t < Duration(Current)) return;
                Current = GhostGesture.None;
                manual = false;
            }
            if (!eligible || !autonomousEnabled) return;
            if (next < 0)
                next = now + (lastChoice == GhostGesture.None ? 15 + random.NextDouble()*12 : 23 + random.NextDouble()*20);
            if (now >= next)
            {
                lastChoice = Choose(now);
                Start(lastChoice,now,false);
            }
        }
    }
}
