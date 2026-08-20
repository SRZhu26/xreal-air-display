using System;
using System.Text.Json.Serialization;

namespace PhoenixAirViewer.Core
{
    public sealed class PanelSettings
    {
        public const float GentleCurveRadiusMeters = 4.0f;
        public const float WideCurvePanelWidthMeters = 2.4f;
        public const float WideCurvePanelHeightMeters = 1.35f;
        public const float WideCurvePanelDistanceMeters = 1.0f;
        public const float WideCurveRadiusMeters = 4.0f;

        public PanelSettings()
        {
            PanelWidthMeters = 1.6f;
            PanelHeightMeters = 0.9f;
            PanelDistanceMeters = 2.0f;
            CurvatureRadiusXMeters = 0.0f;
            CurvatureRadiusYMeters = 0.0f;
            TranslationSensitivity = 0.0f;
        }

        public float PanelWidthMeters { get; set; }
        public float PanelHeightMeters { get; set; }
        public float PanelDistanceMeters { get; set; }
        public float CurvatureRadiusXMeters { get; set; }
        public float CurvatureRadiusYMeters { get; set; }

        [JsonIgnore]
        public float CurvatureRadiusMeters
        {
            get { return CurvatureRadiusXMeters; }
            set
            {
                CurvatureRadiusXMeters = value;
                CurvatureRadiusYMeters = value;
            }
        }

        public float TranslationSensitivity { get; set; }

        public PanelSettings Clone()
        {
            return new PanelSettings
            {
                PanelWidthMeters = PanelWidthMeters,
                PanelHeightMeters = PanelHeightMeters,
                PanelDistanceMeters = PanelDistanceMeters,
                CurvatureRadiusXMeters = CurvatureRadiusXMeters,
                CurvatureRadiusYMeters = CurvatureRadiusYMeters,
                TranslationSensitivity = TranslationSensitivity
            };
        }

        public static PanelSettings CreateWideCurvedMonitor()
        {
            return new PanelSettings
            {
                PanelWidthMeters = WideCurvePanelWidthMeters,
                PanelHeightMeters = WideCurvePanelHeightMeters,
                PanelDistanceMeters = WideCurvePanelDistanceMeters,
                CurvatureRadiusXMeters = WideCurveRadiusMeters,
                CurvatureRadiusYMeters = WideCurveRadiusMeters,
                TranslationSensitivity = 0.0f
            };
        }

        public void Validate()
        {
            ValidatePositive(PanelWidthMeters, "PanelWidthMeters");
            ValidatePositive(PanelHeightMeters, "PanelHeightMeters");
            ValidatePositive(PanelDistanceMeters, "PanelDistanceMeters");

            ValidateCurvatureRadius(CurvatureRadiusXMeters, PanelWidthMeters * 0.5f, "CurvatureRadiusXMeters");
            ValidateCurvatureRadius(CurvatureRadiusYMeters, PanelHeightMeters * 0.5f, "CurvatureRadiusYMeters");

            if (float.IsNaN(TranslationSensitivity) || float.IsInfinity(TranslationSensitivity) ||
                TranslationSensitivity < PosePipelineSettings.MinimumAxisSensitivity ||
                TranslationSensitivity > PosePipelineSettings.MaximumAxisSensitivity)
            {
                throw new ArgumentOutOfRangeException("TranslationSensitivity");
            }
        }

        private static void ValidatePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateCurvatureRadius(float radius, float halfExtent, string name)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }

            if (radius > 0.0f && radius <= halfExtent)
            {
                throw new ArgumentOutOfRangeException(name, "The curvature radius must be larger than half of the panel extent on its axis.");
            }
        }
    }
}