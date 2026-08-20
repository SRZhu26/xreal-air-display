using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PoseObservation
    {
        public PoseObservation(PoseSample sample, Vector4 nativeComponents, bool hasNativeComponents)
        {
            Sample = sample;
            NativeComponents = nativeComponents;
            HasNativeComponents = hasNativeComponents;
        }

        public PoseSample Sample { get; private set; }
        public long TimestampTicks { get { return Sample.TimestampTicks; } }
        public Quaternion Orientation { get { return Sample.Orientation; } }
        public Vector4 NativeComponents { get; private set; }
        public bool HasNativeComponents { get; private set; }
    }

    public interface IPoseObservationSource
    {
        bool TryGetLatestObservation(out PoseObservation observation);
    }
}