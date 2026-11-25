using System;

namespace AnnaDesktopPet
{
    internal sealed class Uniform2GestureDirector
    {
        internal const double DrowseAfterSeconds = 120;
        private const double BlendSeconds = .22;
        private readonly Random random;
        private Uniform2Gesture lastIdle;
        private Uniform2GesturePose from;
        private double previousTick = -1, quietSince = -1, nextIdle = -1;
        private double started, release = -1, nextNod, nextDrowse;

        internal Uniform2Gesture Current { get; private set; }
        internal Uniform2GesturePose Pose { get; private set; }
        internal Uniform2GestureDirector(Random random) { this.random = random; }

        internal void Reset()
        {
            Current = lastIdle = Uniform2Gesture.None;
            Pose = from = new Uniform2GesturePose();
            previousTick = quietSince = nextIdle = release = -1;
            nextNod = nextDrowse = 0;
        }

        internal void NoteInteraction(double now)
        {
            if (quietSince >= 0) lastIdle = Uniform2Gesture.None;
            quietSince = nextIdle = -1;
            if (Current == Uniform2Gesture.Peek || Current == Uniform2Gesture.Drowse)
                StartRelease(now);
        }

        private void StartRelease(double now)
        {
            from = Pose;
            release = now;
            Current = Uniform2Gesture.None;
        }

        internal void EndChatNod(double now)
        {
            if (Current == Uniform2Gesture.Nod) StartRelease(now);
        }

        private void Start(Uniform2Gesture kind, double now, Uniform2GesturePose previous)
        {
            from = previous;
            Current = kind;
            started = now;
            release = -1;
            Pose = PoseAt(now);
        }

        internal bool TryNod(double now, IdleGesturePose ordinary = default(IdleGesturePose))
        {
            if (Current == Uniform2Gesture.Nod || now < nextNod) return false;
            var previous = Pose.Face.ControlsGaze ? Pose : new Uniform2GesturePose { Face = ordinary };
            Start(Uniform2Gesture.Nod, now, previous);
            nextNod = now + 12;
            previousTick = now;
            quietSince = nextIdle = -1;
            lastIdle = Uniform2Gesture.None;
            return true;
        }

        private static double BlendAmount(double dt)
        {
            double x = Math.Max(0, Math.Min(1, dt / BlendSeconds));
            return x*x*(3-2*x);
        }

        private Uniform2GesturePose PoseAt(double now)
        {
            var target = Uniform2Gestures.Sample(Current, now-started);
            if (!from.Face.ControlsGaze || now-started >= BlendSeconds) return target;
            double blend = BlendAmount(now-started);
            return new Uniform2GesturePose {
                Face = IdleGesturePose.Blend(from.Face, target.Face, blend),
                Turn = from.Turn + (target.Turn-from.Turn)*blend
            };
        }

        internal void Update(double now, bool quiet, bool canNod, bool idleChannelFree)
        {
            if (previousTick >= 0 && (now < previousTick || now-previousTick > .75)) Reset();
            previousTick = now;
            if (quiet && quietSince < 0)
            {
                quietSince = now;
                nextIdle = now + 30 + 17*random.NextDouble();
            }
            if (!quiet) NoteInteraction(now);
            if (Current == Uniform2Gesture.Nod && !canNod) StartRelease(now);
            if (Current != Uniform2Gesture.None)
            {
                Pose = PoseAt(now);
                if (now-started < Uniform2Gestures.Duration(Current)) return;
                Current = Uniform2Gesture.None;
                Pose = new Uniform2GesturePose();
            }
            if (release >= 0)
            {
                if (now-release < BlendSeconds)
                {
                    Pose = from.Scale(1-BlendAmount(now-release));
                    return;
                }
                release = -1;
                Pose = new Uniform2GesturePose();
            }
            if (!quiet || !idleChannelFree || now < nextIdle) return;
            bool sleepy = now-quietSince >= DrowseAfterSeconds && now >= nextDrowse;
            Uniform2Gesture choice = sleepy && lastIdle != Uniform2Gesture.Drowse
                ? Uniform2Gesture.Drowse : Uniform2Gesture.Peek;
            if (choice == lastIdle)
            {
                nextIdle = Math.Max(quietSince+DrowseAfterSeconds, nextDrowse);
                return;
            }
            Start(choice, now, new Uniform2GesturePose());
            lastIdle = choice;
            if (choice == Uniform2Gesture.Drowse) nextDrowse = now + 150;
            nextIdle = now + 37 + 20*random.NextDouble();
        }
    }
}
