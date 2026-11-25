using System;

namespace AnnaDesktopPet
{
    internal enum Uniform2Gesture { None, Peek, Nod, Drowse }

    internal struct Uniform2GesturePose
    {
        internal IdleGesturePose Face;
        internal double Turn;
        internal double EntryCarry;

        internal Uniform2GesturePose Scale(double amount)
        {
            return new Uniform2GesturePose {
                Face = Face.Scale(amount), Turn = Turn * amount, EntryCarry = EntryCarry * amount
            };
        }
    }

    internal static class Uniform2Gestures
    {
        internal static double Duration(Uniform2Gesture kind)
        {
            return kind == Uniform2Gesture.Peek || kind == Uniform2Gesture.Nod || kind == Uniform2Gesture.Drowse ? 4 : 0;
        }

        // Quintic easing comes to rest at both ends of every beat.
        private static double Ease(double time, double start, double end)
        {
            double x = Math.Max(0, Math.Min(1, (time-start)/(end-start)));
            return x*x*x*(x*(x*6-15)+10);
        }

        private static double Arc(double time, double start, double peak, double release, double end)
        {
            return Ease(time,start,peak)-Ease(time,release,end);
        }

        internal static Uniform2GesturePose Sample(Uniform2Gesture kind, double t)
        {
            var shot = new Uniform2GesturePose();
            if (!(t >= 0 && t < Duration(kind))) return shot;
            shot.Face.ControlsGaze = true;
            shot.EntryCarry = 1-Ease(t,0,.24);
            switch (kind)
            {
                case Uniform2Gesture.Peek:
                    // Her eyes come back before the head; she tucks her chin and smiles.
                    shot.Face.EyeLook = -.94*Arc(t,.14,.41,1.50,1.80);
                    shot.Face.DownLook = -.68*Arc(t,1.55,1.90,2.43,2.98);
                    shot.Turn = -.85*Arc(t,.39,1.10,1.84,2.53);
                    shot.Face.ListenTilt = -.40*Arc(t,.47,1.24,1.89,2.70)
                                         + .10*Arc(t,2.06,2.61,2.89,3.57);
                    shot.Face.HeadPitch = .22*Arc(t,1.64,2.22,2.67,3.31);
                    shot.Face.SnackSmile = .71*Arc(t,1.84,2.63,2.81,3.72);
                    break;
                case Uniform2Gesture.Nod:
                    // Look up, give one considered nod, then a shallower acknowledgement.
                    shot.Face.DownLook = -.72*Arc(t,.08,.39,.66,1.03);
                    shot.Face.HeadPitch = -.20*Arc(t,.28,.65,.69,.99)
                                        + 1.07*Arc(t,.74,1.25,1.42,1.89)
                                        + .61*Arc(t,1.98,2.29,2.42,2.84);
                    shot.Face.DownLook += .51*Arc(t,.79,1.18,1.43,1.79)
                                        + .23*Arc(t,2.01,2.23,2.42,2.73);
                    shot.Face.EyeClosure = .14*Arc(t,.94,1.20,1.40,1.71)
                                         + .07*Arc(t,2.10,2.29,2.40,2.58);
                    break;
                case Uniform2Gesture.Drowse:
                    // Heavy eyelids, a beat fully asleep, then the eyes wake ahead of the head.
                    shot.Face.DownLook = .82*Arc(t,.12,1.20,2.14,2.51)
                                       - .43*Arc(t,2.27,2.67,2.89,3.46);
                    shot.Face.EyeClosure = .31*Arc(t,.25,1.13,2.10,2.58)
                                         + .69*Arc(t,1.22,1.66,2.11,2.46);
                    shot.Face.HeadPitch = .51*Arc(t,.42,1.41,2.16,2.89)
                                        + .66*Arc(t,1.57,1.95,2.13,2.56)
                                        - .22*Arc(t,2.38,2.76,2.84,3.53);
                    shot.Face.ListenTilt = .24*Arc(t,.78,1.87,2.17,2.95);
                    break;
            }
            return shot;
        }
    }
}
