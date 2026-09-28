using System;

namespace TcpEcho.Shared
{
    /// <summary>Shared randomness: interval jitter and random slices of the event text.</summary>
    public static class Jitter
    {
        private static readonly Random Rng = new Random();
        private static readonly object Gate = new object();

        /// <summary>baseInterval scaled by a uniform factor in [1 - fraction, 1 + fraction].</summary>
        public static TimeSpan Next(TimeSpan baseInterval, double fraction)
        {
            double factor;
            lock (Gate)
            {
                factor = 1.0 + ((Rng.NextDouble() * 2.0) - 1.0) * fraction;
            }
            double ms = baseInterval.TotalMilliseconds * factor;
            return TimeSpan.FromMilliseconds(Math.Max(1.0, ms));
        }

        public static int NextInt(int minInclusive, int maxExclusive)
        {
            lock (Gate)
            {
                return Rng.Next(minInclusive, maxExclusive);
            }
        }

        /// <summary>Picks a random run of 1-3 consecutive lines out of the supplied text.</summary>
        public static string NextFragment(string[] lines)
        {
            if (lines == null || lines.Length == 0)
                return string.Empty;

            int take = Math.Min(NextInt(1, 4), lines.Length);
            int start = NextInt(0, lines.Length - take + 1);
            return string.Join(Environment.NewLine, lines, start, take);
        }
    }
}
