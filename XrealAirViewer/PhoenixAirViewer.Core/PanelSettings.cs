using System;

namespace PhoenixAirViewer.Core
{
    public sealed class PanelSettings
    {
        public PanelSettings()
        {
            PanelWidthMeters = 1.6f;
            PanelHeightMeters = 0.9f;
            PanelDistanceMeters = 2.0f;
            CurvatureRadiusMeters = 0.0f;
        }

        public float PanelWidthMeters { get; set; }
        public float PanelHeightMeters { get; set; }
        public float PanelDistanceMeters { get; set; }
        public float CurvatureRadiusMeters { get; set; }

        public PanelSettings Clone()
        {
            return new PanelSettings
            {
                PanelWidthMeters = PanelWidthMeters,
                PanelHeightMeters = PanelHeightMeters,
                PanelDistanceMeters = PanelDistanceMeters,
                CurvatureRadiusMeters = CurvatureRadiusMeters
            };
        }

        public void Validate()
        {
            ValidatePositive(PanelWidthMeters, "PanelWidthMeters");
            ValidatePositive(PanelHeightMeters, "PanelHeightMeters");
            ValidatePositive(PanelDistanceMeters, "PanelDistanceMeters");

            if (float.IsNaN(CurvatureRadiusMeters) || float.IsInfinity(CurvatureRadiusMeters) || CurvatureRadiusMeters < 0.0f)
            {
                throw new ArgumentOutOfRangeException("CurvatureRadiusMeters");
            }

            if (CurvatureRadiusMeters > 0.0f && CurvatureRadiusMeters < PanelWidthMeters / (float)Math.PI)
            {
                throw new ArgumentOutOfRangeException("CurvatureRadiusMeters", "The curvature radius is too small for the panel width.");
            }
        }

        private static void ValidatePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }
}