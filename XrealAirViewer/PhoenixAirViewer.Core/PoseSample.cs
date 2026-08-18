using System;
using System.Diagnostics;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public struct PoseSample
    {
        public PoseSample(long timestampTicks, Quaternion orientation) : this()
        {
            Quaternion normalized;
            if (!PoseMath.TryNormalize(orientation, out normalized))
            {
                throw new ArgumentException("The pose orientation must be a finite, non-zero quaternion.", "orientation");
            }

            TimestampTicks = timestampTicks;
            Orientation = normalized;
        }

        public long TimestampTicks { get; private set; }
        public Quaternion Orientation { get; private set; }

        public double AgeSeconds(long nowTicks)
        {
            return PoseClock.SecondsBetween(TimestampTicks, nowTicks);
        }
    }

    public static class PoseClock
    {
        public static long NowTicks()
        {
            return Stopwatch.GetTimestamp();
        }

        public static double SecondsBetween(long earlierTicks, long laterTicks)
        {
            return (laterTicks - earlierTicks) / (double)Stopwatch.Frequency;
        }
    }
}
