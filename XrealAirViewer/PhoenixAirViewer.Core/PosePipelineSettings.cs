using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePipelineSettings
    {
        public PosePipelineSettings()
        {
            SmoothingTimeConstantSeconds = 0.035f;
            MaxAngularVelocityDegreesPerSecond = 720.0f;
            SensorToRenderer = Quaternion.Identity;
            AutoRecenterOnFirstSample = true;
        }

        public float SmoothingTimeConstantSeconds { get; set; }
        public float MaxAngularVelocityDegreesPerSecond { get; set; }
        public bool HorizonLock { get; set; }
        public bool RollLock { get; set; }
        public Quaternion SensorToRenderer { get; set; }
        public bool AutoRecenterOnFirstSample { get; set; }

        public PosePipelineSettings Clone()
        {
            return new PosePipelineSettings
            {
                SmoothingTimeConstantSeconds = SmoothingTimeConstantSeconds,
                MaxAngularVelocityDegreesPerSecond = MaxAngularVelocityDegreesPerSecond,
                HorizonLock = HorizonLock,
                RollLock = RollLock,
                SensorToRenderer = SensorToRenderer,
                AutoRecenterOnFirstSample = AutoRecenterOnFirstSample
            };
        }

        public void Validate()
        {
            if (float.IsNaN(SmoothingTimeConstantSeconds) || float.IsInfinity(SmoothingTimeConstantSeconds) || SmoothingTimeConstantSeconds < 0.0f)
            {
                throw new ArgumentOutOfRangeException("SmoothingTimeConstantSeconds");
            }

            if (float.IsNaN(MaxAngularVelocityDegreesPerSecond) || float.IsInfinity(MaxAngularVelocityDegreesPerSecond) || MaxAngularVelocityDegreesPerSecond < 0.0f)
            {
                throw new ArgumentOutOfRangeException("MaxAngularVelocityDegreesPerSecond");
            }

            SensorToRenderer = PoseMath.Normalize(SensorToRenderer);
        }
    }
}
