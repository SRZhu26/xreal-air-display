using System;
using System.Numerics;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public static class PanelGeometry
    {
        public static Vector3 CreatePosition(PanelSettings panelSettings, float textureX, float textureY)
        {
            if (panelSettings == null)
            {
                throw new ArgumentNullException("panelSettings");
            }

            float positionX = (textureX - 0.5f) * panelSettings.PanelWidthMeters;
            float positionY = (panelSettings.PanelHeightMeters * 0.5f) - textureY * panelSettings.PanelHeightMeters;
            if (panelSettings.CurvatureRadiusXMeters <= 0.0f && panelSettings.CurvatureRadiusYMeters <= 0.0f)
            {
                return new Vector3(
                    positionX,
                    positionY,
                    -panelSettings.PanelDistanceMeters);
            }

            float horizontalDepth = CalculateCurvatureDepth(positionX, panelSettings.CurvatureRadiusXMeters);
            float verticalDepth = CalculateCurvatureDepth(positionY, panelSettings.CurvatureRadiusYMeters);
            float positionZ = -panelSettings.PanelDistanceMeters +
                horizontalDepth +
                verticalDepth;
            return new Vector3(positionX, positionY, positionZ);
        }

        private static float CalculateCurvatureDepth(float position, float radius)
        {
            if (radius <= 0.0f)
            {
                return 0.0f;
            }

            float radiusSquared = radius * radius;
            return radius - (float)Math.Sqrt(Math.Max(0.0f, radiusSquared - position * position));
        }
    }
}