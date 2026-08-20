using System;
using System.Numerics;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public static class PanelViewTransform
    {
        public static Matrix4x4 CreateWorldLockedView(Quaternion cameraOrientation)
        {
            Quaternion normalizedOrientation = PoseMath.Normalize(cameraOrientation);
            return Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(normalizedOrientation));
        }

        public static Matrix4x4 CreateProjection(uint width, uint height)
        {
            if (width == 0)
            {
                throw new ArgumentOutOfRangeException("width");
            }

            if (height == 0)
            {
                throw new ArgumentOutOfRangeException("height");
            }

            return Matrix4x4.CreatePerspectiveFieldOfView(
                (float)(Math.PI / 3.0),
                width / (float)height,
                0.05f,
                100.0f);
        }

        public static Matrix4x4 CreateWorldViewProjection(Quaternion cameraOrientation, uint width, uint height)
        {
            return CreateWorldViewProjection(cameraOrientation, Vector3.Zero, false, width, height);
        }

        public static Matrix4x4 CreateWorldViewProjection(
            Quaternion cameraOrientation,
            Vector3 worldOffset,
            bool headFollowing,
            uint width,
            uint height)
        {
            Quaternion normalizedOrientation = PoseMath.Normalize(cameraOrientation);
            Matrix4x4 view = headFollowing
                ? Matrix4x4.CreateFromQuaternion(normalizedOrientation)
                : Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(normalizedOrientation));
            return Matrix4x4.CreateTranslation(worldOffset) * view * CreateProjection(width, height);
        }

        public static Vector3 CreateAngleTranslation(
            Quaternion cameraOrientation,
            float panelDistanceMeters,
            float translationSensitivity)
        {
            if (float.IsNaN(panelDistanceMeters) || float.IsInfinity(panelDistanceMeters) || panelDistanceMeters <= 0.0f)
            {
                throw new ArgumentOutOfRangeException("panelDistanceMeters");
            }

            if (float.IsNaN(translationSensitivity) || float.IsInfinity(translationSensitivity) ||
                translationSensitivity < PosePipelineSettings.MinimumAxisSensitivity ||
                translationSensitivity > PosePipelineSettings.MaximumAxisSensitivity)
            {
                throw new ArgumentOutOfRangeException("translationSensitivity");
            }

            if (Math.Abs(translationSensitivity) <= 0.000001f)
            {
                return Vector3.Zero;
            }

            Quaternion pitchYawOrientation = PoseMath.RemoveTwistAroundAxis(cameraOrientation, Vector3.UnitZ);
            Vector3 rotationVector = PoseMath.ToRotationVector(pitchYawOrientation);
            float scaledYaw = Clamp(-rotationVector.Y * translationSensitivity * 0.35f, -0.6f, 0.6f);
            float scaledPitch = Clamp(rotationVector.X * translationSensitivity * 0.35f, -0.6f, 0.6f);
            return new Vector3(
                panelDistanceMeters * (float)Math.Tan(scaledYaw),
                -panelDistanceMeters * (float)Math.Tan(scaledPitch),
                0.0f);
        }

        public static Vector3 ProjectToNdc(Vector3 worldPoint, Matrix4x4 worldViewProjection)
        {
            Vector4 clipPoint = Vector4.Transform(new Vector4(worldPoint, 1.0f), worldViewProjection);
            if (Math.Abs(clipPoint.W) <= 0.0000001f)
            {
                throw new InvalidOperationException("The point cannot be projected because its clip-space W is zero.");
            }

            return new Vector3(
                clipPoint.X / clipPoint.W,
                clipPoint.Y / clipPoint.W,
                clipPoint.Z / clipPoint.W);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}