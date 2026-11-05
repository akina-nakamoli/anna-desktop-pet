using System;
namespace AnnaDesktopPet
{
    // Deterministic time-based movement; coordinates are window DIPs, not screen pixels.
    internal sealed class AutonomousMotion
    {
        private double nextAt = 25, endAt, lastAt, destination;
        private readonly Random random = new Random();
        public bool Walking { get; private set; }
        public int Direction { get; private set; }
        public double StartedAt { get; private set; }
        public double Update(double t, double left, double width, double min, double max, bool allowed)
        {
            if (!allowed || max - min < width || left < min - 1 || left > max - width + 1)
            { nextAt = Math.Max(nextAt, t + 25); Walking = false; return left; }
            if (!Walking)
            {
                if (t < nextAt) return left;
                double maxLeft = max - width;
                Direction = random.Next(2) == 0 ? -1 : 1;
                if (Direction > 0 && maxLeft - left < 30) Direction = -1;
                if (Direction < 0 && left - min < 30) Direction = 1;
                destination = Math.Max(min, Math.Min(maxLeft, left + Direction * (55 + random.NextDouble() * 45)));
                if (Math.Abs(destination - left) < 10) { nextAt = t + 45; return left; }
                Walking = true; StartedAt = lastAt = t; endAt = t + 5;
                return left;
            }
            double dt = Math.Max(0, Math.Min(.1, t - lastAt)); lastAt = t;
            double step = 27 * dt;
            double next = Direction > 0 ? Math.Min(destination, left + step) : Math.Max(destination, left - step);
            if (Math.Abs(destination - next) < .1 || t >= endAt)
            { Walking = false; nextAt = t + 55 + random.NextDouble() * 35; }
            return next;
        }
        public void Interrupt(double t) { Walking = false; nextAt = t + 30; }
    }
}