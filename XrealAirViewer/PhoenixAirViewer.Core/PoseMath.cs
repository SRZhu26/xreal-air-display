using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public static class PoseMath
    {
        public static bool TryNormalize(Quaternion value, out Quaternion normalized)
        {
            float lengthSquared = Quaternion.Dot(value, value);
            if (float.IsNaN(lengthSquared) || float.IsInfinity(lengthSquared) || lengthSquared <= 0.0000000001f)
            {
                normalized = Quaternion.Identity;
                return false;
            }

            float inverseLength = 1.0f / (float)Math.Sqrt(lengthSquared);
            normalized = new Quaternion(
                value.X * inverseLength,
                value.Y * inverseLength,
                value.Z * inverseLength,
                value.W * inverseLength);
            return true;
        }

        public static Quaternion Normalize(Quaternion value)
        {
            Quaternion normalized;
            if (!TryNormalize(value, out normalized))
            {
                throw new ArgumentException("The quaternion must be finite and non-zero.", "value");
            }

            return normalized;
        }

        public static Quaternion MapBasis(Quaternion orientation, Quaternion sensorToRenderer)
        {
            Quaternion mapped = Quaternion.Multiply(
                Quaternion.Multiply(sensorToRenderer, orientation),
                Quaternion.Inverse(sensorToRenderer));
            return Normalize(mapped);
        }

        public static Quaternion ApplyAxisSensitivity(
            Quaternion orientation,
            float pitchSensitivity,
            float yawSensitivity,
            float rollSensitivity)
        {
            Quaternion normalizedOrientation = Normalize(orientation);
            if (normalizedOrientation.W < 0.0f)
            {
                normalizedOrientation = new Quaternion(
                    -normalizedOrientation.X,
                    -normalizedOrientation.Y,
                    -normalizedOrientation.Z,
                    -normalizedOrientation.W);
            }

            Vector3 vectorPart = new Vector3(
                normalizedOrientation.X,
                normalizedOrientation.Y,
                normalizedOrientation.Z);
            float sineHalfAngle = vectorPart.Length();
            if (sineHalfAngle <= 0.000001f)
            {
                return Quaternion.Identity;
            }

            float scalar = Math.Min(1.0f, Math.Max(-1.0f, normalizedOrientation.W));
            float angle = 2.0f * (float)Math.Acos(scalar);
            Vector3 rotationVector = vectorPart * (angle / sineHalfAngle);
            Vector3 scaledRotationVector = new Vector3(
                rotationVector.X * pitchSensitivity,
                rotationVector.Y * yawSensitivity,
                rotationVector.Z * rollSensitivity);
            float scaledAngle = scaledRotationVector.Length();
            if (scaledAngle <= 0.000001f)
            {
                return Quaternion.Identity;
            }

            return Normalize(Quaternion.CreateFromAxisAngle(scaledRotationVector / scaledAngle, scaledAngle));
        }

        public static float AngularVelocityDegreesPerSecond(Quaternion from, Quaternion to, double deltaSeconds)
        {
            if (deltaSeconds <= 0.000001)
            {
                return 0.0f;
            }

            return AngularDistanceRadians(from, to) * 180.0f / (float)Math.PI / (float)deltaSeconds;
        }

        public static Vector3 ToRotationVector(Quaternion orientation)
        {
            Quaternion normalizedOrientation = Normalize(orientation);
            if (normalizedOrientation.W < 0.0f)
            {
                normalizedOrientation = new Quaternion(
                    -normalizedOrientation.X,
                    -normalizedOrientation.Y,
                    -normalizedOrientation.Z,
                    -normalizedOrientation.W);
            }

            Vector3 vectorPart = new Vector3(
                normalizedOrientation.X,
                normalizedOrientation.Y,
                normalizedOrientation.Z);
            float sineHalfAngle = vectorPart.Length();
            if (sineHalfAngle <= 0.000001f)
            {
                return Vector3.Zero;
            }

            float scalar = Math.Min(1.0f, Math.Max(-1.0f, normalizedOrientation.W));
            float angle = 2.0f * (float)Math.Acos(scalar);
            return vectorPart * (angle / sineHalfAngle);
        }

        public static float AngularDistanceRadians(Quaternion from, Quaternion to)
        {
            Quaternion normalizedFrom = Normalize(from);
            Quaternion normalizedTo = Normalize(to);
            float dot = Math.Abs(Quaternion.Dot(normalizedFrom, normalizedTo));
            dot = Math.Max(-1.0f, Math.Min(1.0f, dot));
            return 2.0f * (float)Math.Acos(dot);
        }

        public static Quaternion SlerpShortest(Quaternion from, Quaternion to, float amount)
        {
            Quaternion normalizedFrom = Normalize(from);
            Quaternion normalizedTo = Normalize(to);
            float dot = Quaternion.Dot(normalizedFrom, normalizedTo);

            if (dot < 0.0f)
            {
                normalizedTo = new Quaternion(-normalizedTo.X, -normalizedTo.Y, -normalizedTo.Z, -normalizedTo.W);
                dot = -dot;
            }

            dot = Math.Max(-1.0f, Math.Min(1.0f, dot));
            amount = Math.Max(0.0f, Math.Min(1.0f, amount));

            if (dot > 0.9995f)
            {
                Quaternion linear = new Quaternion(
                    normalizedFrom.X + amount * (normalizedTo.X - normalizedFrom.X),
                    normalizedFrom.Y + amount * (normalizedTo.Y - normalizedFrom.Y),
                    normalizedFrom.Z + amount * (normalizedTo.Z - normalizedFrom.Z),
                    normalizedFrom.W + amount * (normalizedTo.W - normalizedFrom.W));
                return Normalize(linear);
            }

            float angle = (float)Math.Acos(dot);
            float sine = (float)Math.Sin(angle);
            float fromWeight = (float)Math.Sin((1.0f - amount) * angle) / sine;
            float toWeight = (float)Math.Sin(amount * angle) / sine;
            Quaternion result = new Quaternion(
                fromWeight * normalizedFrom.X + toWeight * normalizedTo.X,
                fromWeight * normalizedFrom.Y + toWeight * normalizedTo.Y,
                fromWeight * normalizedFrom.Z + toWeight * normalizedTo.Z,
                fromWeight * normalizedFrom.W + toWeight * normalizedTo.W);
            return Normalize(result);
        }

        public static Quaternion RotateToward(Quaternion from, Quaternion to, float maximumAngleRadians)
        {
            float angle = AngularDistanceRadians(from, to);
            if (angle <= maximumAngleRadians || angle <= 0.000001f)
            {
                return Normalize(to);
            }

            return SlerpShortest(from, to, maximumAngleRadians / angle);
        }

        public static Quaternion RemoveTwistAroundAxis(Quaternion orientation, Vector3 axis)
        {
            Quaternion normalizedOrientation = Normalize(orientation);
            if (axis.LengthSquared() <= 0.0000001f)
            {
                throw new ArgumentException("The twist axis must be non-zero.", "axis");
            }

            Vector3 normalizedAxis = Vector3.Normalize(axis);
            Vector3 vectorPart = new Vector3(normalizedOrientation.X, normalizedOrientation.Y, normalizedOrientation.Z);
            Vector3 projected = normalizedAxis * Vector3.Dot(vectorPart, normalizedAxis);
            Quaternion twist = new Quaternion(projected, normalizedOrientation.W);
            if (!TryNormalize(twist, out twist))
            {
                return normalizedOrientation;
            }

            return Normalize(Quaternion.Multiply(normalizedOrientation, Quaternion.Inverse(twist)));
        }

        public static Quaternion RemoveRollAroundForward(Quaternion orientation, Vector3 worldUp)
        {
            Quaternion normalizedOrientation = Normalize(orientation);
            if (worldUp.LengthSquared() <= 0.0000001f)
            {
                throw new ArgumentException("The world-up axis must be non-zero.", "worldUp");
            }

            Vector3 upReference = Vector3.Normalize(worldUp);
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, normalizedOrientation);
            Vector3 right = Vector3.Cross(forward, upReference);
            if (right.LengthSquared() <= 0.0000001f)
            {
                right = Vector3.Transform(Vector3.UnitX, normalizedOrientation);
                right -= forward * Vector3.Dot(right, forward);
            }

            if (right.LengthSquared() <= 0.0000001f)
            {
                right = Vector3.Cross(forward, Vector3.UnitX);
                if (right.LengthSquared() <= 0.0000001f)
                {
                    right = Vector3.Cross(forward, Vector3.UnitZ);
                }
            }

            right = Vector3.Normalize(right);
            Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
            Vector3 back = -forward;
            Matrix4x4 rotation = new Matrix4x4(
                right.X, right.Y, right.Z, 0.0f,
                up.X, up.Y, up.Z, 0.0f,
                back.X, back.Y, back.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            return Normalize(Quaternion.CreateFromRotationMatrix(rotation));
        }
    }
}
