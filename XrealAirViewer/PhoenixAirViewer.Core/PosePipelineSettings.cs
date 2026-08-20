using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePipelineSettings
    {
        public const float MinimumAxisSensitivity = -2.0f;
        public const float MaximumAxisSensitivity = 2.0f;
        public const float DefaultPitchSensitivity = 1.0f;
        public const float DefaultYawSensitivity = -1.0f;
        public const float DefaultRollSensitivity = -1.0f;
        public const float DefaultYawDriftRateDegreesPerSecond = -0.11f;
        public const float DefaultPitchDriftRateDegreesPerSecond = 0.0f;
        public const float DefaultPoseStabilityLimitDegreesPerSecond = 900.0f;
        public const float MinimumDriftRateDegreesPerSecond = -10.0f;
        public const float MaximumDriftRateDegreesPerSecond = 10.0f;

        public static Quaternion LegacyDefaultAirSensorToRenderer
        {
            get { return new Quaternion(-0.5f, -0.5f, -0.5f, 0.5f); }
        }

        public static Quaternion DefaultAirSensorToRenderer
        {
            get { return new Quaternion(0.0f, 0.70710677f, -0.70710677f, 0.0f); }
        }

        public PosePipelineSettings()
        {
            SmoothingTimeConstantSeconds = 0.0f;
            MaxAngularVelocityDegreesPerSecond = 0.0f;
            PoseStabilityLimitDegreesPerSecond = DefaultPoseStabilityLimitDegreesPerSecond;
            PitchSensitivity = DefaultPitchSensitivity;
            YawSensitivity = DefaultYawSensitivity;
            RollSensitivity = DefaultRollSensitivity;
            YawDriftRateDegreesPerSecond = DefaultYawDriftRateDegreesPerSecond;
            PitchDriftRateDegreesPerSecond = DefaultPitchDriftRateDegreesPerSecond;
            SensorToRenderer = DefaultAirSensorToRenderer;
            AutoRecenterDelaySeconds = 3.5f;
            AutoRecenterOnFirstSample = true;
        }

        public float SmoothingTimeConstantSeconds { get; set; }
        public float MaxAngularVelocityDegreesPerSecond { get; set; }
        public float PoseStabilityLimitDegreesPerSecond { get; set; }
        public float PitchSensitivity { get; set; }
        public float YawSensitivity { get; set; }
        public float RollSensitivity { get; set; }
        public float YawDriftRateDegreesPerSecond { get; set; }
        public float PitchDriftRateDegreesPerSecond { get; set; }
        public bool HorizonLock { get; set; }
        public bool RollLock { get; set; }
        public Quaternion SensorToRenderer { get; set; }
        public float AutoRecenterDelaySeconds { get; set; }
        public bool AutoRecenterOnFirstSample { get; set; }

        public PosePipelineSettings Clone()
        {
            return new PosePipelineSettings
            {
                SmoothingTimeConstantSeconds = SmoothingTimeConstantSeconds,
                MaxAngularVelocityDegreesPerSecond = MaxAngularVelocityDegreesPerSecond,
                PoseStabilityLimitDegreesPerSecond = PoseStabilityLimitDegreesPerSecond,
                PitchSensitivity = PitchSensitivity,
                YawSensitivity = YawSensitivity,
                RollSensitivity = RollSensitivity,
                YawDriftRateDegreesPerSecond = YawDriftRateDegreesPerSecond,
                PitchDriftRateDegreesPerSecond = PitchDriftRateDegreesPerSecond,
                HorizonLock = HorizonLock,
                RollLock = RollLock,
                SensorToRenderer = SensorToRenderer,
                AutoRecenterDelaySeconds = AutoRecenterDelaySeconds,
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

            if (float.IsNaN(PoseStabilityLimitDegreesPerSecond) ||
                float.IsInfinity(PoseStabilityLimitDegreesPerSecond) ||
                PoseStabilityLimitDegreesPerSecond < 0.0f)
            {
                throw new ArgumentOutOfRangeException("PoseStabilityLimitDegreesPerSecond");
            }

            ValidateAxisSensitivity(PitchSensitivity, "PitchSensitivity");
            ValidateAxisSensitivity(YawSensitivity, "YawSensitivity");
            ValidateAxisSensitivity(RollSensitivity, "RollSensitivity");

            ValidateRange(YawDriftRateDegreesPerSecond, MinimumDriftRateDegreesPerSecond, MaximumDriftRateDegreesPerSecond, "YawDriftRateDegreesPerSecond");
            ValidateRange(PitchDriftRateDegreesPerSecond, MinimumDriftRateDegreesPerSecond, MaximumDriftRateDegreesPerSecond, "PitchDriftRateDegreesPerSecond");

            if (float.IsNaN(AutoRecenterDelaySeconds) || float.IsInfinity(AutoRecenterDelaySeconds) || AutoRecenterDelaySeconds < 0.0f)
            {
                throw new ArgumentOutOfRangeException("AutoRecenterDelaySeconds");
            }

            SensorToRenderer = PoseMath.Normalize(SensorToRenderer);
        }

        private static void ValidateAxisSensitivity(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < MinimumAxisSensitivity || value > MaximumAxisSensitivity)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidatePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}
