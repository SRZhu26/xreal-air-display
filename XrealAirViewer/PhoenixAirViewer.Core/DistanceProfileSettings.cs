using System;
using System.Collections.Generic;

namespace PhoenixAirViewer.Core
{
    public sealed class DistanceProfileSettings
    {
        public DistanceProfileSettings()
        {
            Key = "Mid";
            DisplayName = "Mid";
            PanelDistanceMeters = 0.85f;
            PitchSensitivity = PosePipelineSettings.DefaultPitchSensitivity;
            YawSensitivity = PosePipelineSettings.DefaultYawSensitivity;
            RollSensitivity = PosePipelineSettings.DefaultRollSensitivity;
            TranslationSensitivity = 0.0f;
        }

        public string Key { get; set; }
        public string DisplayName { get; set; }
        public float PanelDistanceMeters { get; set; }
        public float PitchSensitivity { get; set; }
        public float YawSensitivity { get; set; }
        public float RollSensitivity { get; set; }
        public float TranslationSensitivity { get; set; }

        public DistanceProfileSettings Clone()
        {
            return new DistanceProfileSettings
            {
                Key = Key,
                DisplayName = DisplayName,
                PanelDistanceMeters = PanelDistanceMeters,
                PitchSensitivity = PitchSensitivity,
                YawSensitivity = YawSensitivity,
                RollSensitivity = RollSensitivity,
                TranslationSensitivity = TranslationSensitivity
            };
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Key))
            {
                throw new ArgumentException("A distance profile key is required.", "Key");
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                throw new ArgumentException("A distance profile name is required.", "DisplayName");
            }

            ValidatePositive(PanelDistanceMeters, "PanelDistanceMeters");
            ValidateSensitivity(PitchSensitivity, "PitchSensitivity");
            ValidateSensitivity(YawSensitivity, "YawSensitivity");
            ValidateSensitivity(RollSensitivity, "RollSensitivity");
            ValidateSensitivity(TranslationSensitivity, "TranslationSensitivity");
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public static IList<DistanceProfileSettings> CreateDefaults(PosePipelineSettings poseSettings)
        {
            PosePipelineSettings source = poseSettings ?? new PosePipelineSettings();
            return new List<DistanceProfileSettings>
            {
                Create("Near", "Near", 0.7f, source),
                Create("Mid", "Mid", 0.85f, source),
                Create("Far", "Far", 1.0f, source),
                Create("Furthest", "Furthest", 1.2f, source)
            };
        }

        private static DistanceProfileSettings Create(string key, string displayName, float distance, PosePipelineSettings source)
        {
            return new DistanceProfileSettings
            {
                Key = key,
                DisplayName = displayName,
                PanelDistanceMeters = distance,
                PitchSensitivity = source.PitchSensitivity,
                YawSensitivity = source.YawSensitivity,
                RollSensitivity = source.RollSensitivity,
                TranslationSensitivity = 0.0f
            };
        }

        private static void ValidatePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateSensitivity(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < PosePipelineSettings.MinimumAxisSensitivity ||
                value > PosePipelineSettings.MaximumAxisSensitivity)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}