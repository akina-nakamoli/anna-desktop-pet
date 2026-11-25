using System;

namespace AnnaDesktopPet
{
    internal enum IdleGesture { None, WeightShift, Listen, SnackSmile, LookAtShoes, Daydream }

    internal struct IdleGesturePose
    {
        internal double WeightShift, ListenTilt, SnackSmile, HeadPitch;
        internal double EyeLook, DownLook, Daydream, EyeClosure;
        internal bool ControlsGaze;

        internal IdleGesturePose(double shift, double tilt, double smile, double eyes) : this()
        {
            WeightShift = shift;
            ListenTilt = tilt;
            SnackSmile = smile;
            EyeLook = eyes;
        }

        internal double CombineEye(double mouseLook)
        {
            return ControlsGaze ? EyeLook : mouseLook;
        }

        internal double CombineEyeY(double mouseLook)
        {
            return ControlsGaze ? -.80 * DownLook : mouseLook;
        }

        internal double ScreenLeftEyeOpen(double blink)
        {
            return RelaxedEyeOpen(blink);
        }

        internal double ScreenRightEyeOpen(double blink)
        {
            return RelaxedEyeOpen(blink);
        }

        private double RelaxedEyeOpen(double blink)
        {
            return blink * (1 - .22 * SnackSmile - .32 * Daydream) * (1 - EyeClosure);
        }

        private static bool Near(double current, double previous)
        {
            return current == 0 ? previous == 0 : Math.Abs(current - previous) < .001;
        }

        internal bool MatchesGeometry(IdleGesturePose previous)
        {
            return Near(WeightShift, previous.WeightShift) && Near(ListenTilt, previous.ListenTilt)
                && Near(SnackSmile, previous.SnackSmile) && Near(HeadPitch, previous.HeadPitch);
        }

        internal IdleGesturePose Scale(double amount)
        {
            return new IdleGesturePose(WeightShift * amount, ListenTilt * amount, SnackSmile * amount, EyeLook * amount)
            {
                HeadPitch = HeadPitch * amount, DownLook = DownLook * amount,
                Daydream = Daydream * amount, EyeClosure = EyeClosure * amount, ControlsGaze = ControlsGaze
            };
        }
        internal static IdleGesturePose Blend(IdleGesturePose from, IdleGesturePose to, double t)
        {
            if (t >= 1) return to;
            var a = from.Scale(1-t); var b = to.Scale(t);
            return new IdleGesturePose(a.WeightShift+b.WeightShift, a.ListenTilt+b.ListenTilt,
                a.SnackSmile+b.SnackSmile, a.EyeLook+b.EyeLook) {
                HeadPitch=a.HeadPitch+b.HeadPitch, DownLook=a.DownLook+b.DownLook, Daydream=a.Daydream+b.Daydream,
                EyeClosure=a.EyeClosure+b.EyeClosure, ControlsGaze=from.ControlsGaze||to.ControlsGaze
            };
        }
    }

    internal sealed class IdleGestures
    {
        private struct Choice
        {
            internal readonly IdleGesture Kind;
            internal readonly int Weight, Cooldown;
            internal Choice(IdleGesture kind, int weight, int cooldown)
            {
                Kind = kind; Weight = weight; Cooldown = cooldown;
            }
        }

        private static readonly Choice[] choices = {
            new Choice(IdleGesture.WeightShift, 25, 0), new Choice(IdleGesture.Listen, 12, 0),
            new Choice(IdleGesture.SnackSmile, 4, 120), new Choice(IdleGesture.LookAtShoes, 9, 85),
            new Choice(IdleGesture.Daydream, 6, 95)
        };
        private readonly Random random;
        private IdleGesture previous;
        private int direction;
        private const double ReleaseDuration = .25;
        private double started, next = -1, previousTick = -1, release = -1;
        private readonly double[] availableAt = new double[(int)IdleGesture.Daydream + 1];
        private IdleGesturePose releasePose;

        internal IdleGesture Current { get; private set; }
        internal IdleGesturePose Pose { get; private set; }

        internal IdleGestures(Random random) { this.random = random; }

        internal static double Duration(IdleGesture gesture)
        {
            switch (gesture)
            {
                case IdleGesture.WeightShift: return 3.5;
                case IdleGesture.Listen: return 2.65;
                case IdleGesture.SnackSmile: return 2.55;
                case IdleGesture.LookAtShoes: return 3.15;
                case IdleGesture.Daydream: return 5.2;
                default: return 0;
            }
        }

        private static double Ease(double time, double from, double to)
        {
            double x = Math.Max(0, Math.Min(1, (time - from) / (to - from)));
            return x * x * (3 - 2 * x);
        }

        private static double RiseAndFall(double t, double start, double raised, double release, double end)
        {
            return Ease(t, start, raised) - Ease(t, release, end);
        }

        internal static IdleGesturePose Sample(IdleGesture gesture, double time, int side)
        {
            var pose = new IdleGesturePose();
            if (!(time >= 0 && time < Duration(gesture))) return pose;
            pose.ControlsGaze = true;
            switch (gesture)
            {
                case IdleGesture.WeightShift:
                    // Unweight one foot before transferring the hips.
                    double hips = -.08 * Ease(time, 0, .32) + 1.08 * Ease(time, .32, 1.18)
                        - .10 * Ease(time, 1.18, 1.58) - .90 * Ease(time, 2.14, 3.5);
                    pose.WeightShift = side * hips;
                    break;
                case IdleGesture.Listen:
                    pose.EyeLook = side * .25 * (Ease(time, 0, .28) - Ease(time, 1.62, 2.26));
                    pose.ListenTilt = side * (Ease(time, .22, 1.02) - Ease(time, 1.65, 2.65));
                    break;
                case IdleGesture.SnackSmile:
                    pose.EyeLook = side * .15 * RiseAndFall(time, .03, .37, 1.46, 1.98);
                    pose.SnackSmile = Ease(time, .31, .96) - .14 * Ease(time, .96, 1.18)
                        + .11 * Ease(time, 1.18, 1.43) - .97 * Ease(time, 1.64, 2.55);
                    break;
                case IdleGesture.LookAtShoes:
                    pose.DownLook = RiseAndFall(time, 0, .40, 1.97, 2.59);
                    pose.EyeLook = side * .11 * RiseAndFall(time, .04, .38, 1.92, 2.41);
                    pose.HeadPitch = RiseAndFall(time, .23, 1.10, 2.02, 3.15);
                    break;
                case IdleGesture.Daydream:
                    pose.Daydream = RiseAndFall(time, .07, 1.2, 3.82, 5.2);
                    pose.EyeLook = side * .20 * RiseAndFall(time, .18, 1.23, 3.67, 4.57);
                    pose.HeadPitch = -.12 * RiseAndFall(time, .35, 1.6, 3.78, 5.07);
                    break;
            }
            return pose;
        }

        private bool CanStart(Choice choice, double time)
        {
            return choice.Kind != previous && time >= availableAt[(int)choice.Kind];
        }

        private IdleGesture Choose(double time)
        {
            int total = 0;
            foreach (Choice choice in choices)
                if (CanStart(choice, time)) total += choice.Weight;
            int target = random.Next(total);
            foreach (Choice choice in choices)
            {
                if (!CanStart(choice, time)) continue;
                if (target < choice.Weight)
                {
                    availableAt[(int)choice.Kind] = time + choice.Cooldown;
                    return choice.Kind;
                }
                target -= choice.Weight;
            }
            throw new InvalidOperationException("No eligible idle gesture.");
        }

        internal void Reset()
        {
            Current = IdleGesture.None;
            Pose = new IdleGesturePose();
            next = release = previousTick = -1;
        }

        internal void Update(double time, bool eligible)
        {
            // A hidden, suspended or recreated rendering surface starts a fresh idle interval.
            if (previousTick >= 0 && (time < previousTick || time - previousTick > .75)) Reset();
            previousTick = time;
            if (!eligible)
            {
                next = -1;
                if (Current != IdleGesture.None)
                {
                    releasePose = Pose;
                    release = time;
                    Current = IdleGesture.None;
                }
            }
            if (release >= 0)
            {
                if (time < release + ReleaseDuration)
                {
                    Pose = releasePose.Scale(1 - Ease(time, release, release + ReleaseDuration));
                    return;
                }
                Pose = new IdleGesturePose();
                release = -1;
            }
            if (!eligible) return;
            if (Current != IdleGesture.None)
            {
                Pose = Sample(Current, time - started, direction);
                if (time - started < Duration(Current)) return;
                Current = IdleGesture.None;
            }
            if (next < 0)
            {
                next = time + (previous == IdleGesture.None ? 16 + random.NextDouble() * 16 : 20 + random.NextDouble() * 26);
                return;
            }
            if (time < next) return;
            Current = Choose(time);
            previous = Current;
            direction = random.Next(2) == 0 ? -1 : 1;
            started = time;
            Pose = Sample(Current, 0, direction);
            next = -1;
        }
    }
}
