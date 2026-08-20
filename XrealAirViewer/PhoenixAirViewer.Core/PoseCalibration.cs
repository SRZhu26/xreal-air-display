using System;
using System.Collections.Generic;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PoseCalibrationResult
    {
        internal PoseCalibrationResult(Quaternion sensorToRenderer, float axisErrorDegrees, Vector3 sensorYawAxis, Vector3 sensorPitchAxis, Vector3 sensorRollAxis)
        {
            SensorToRenderer = sensorToRenderer;
            AxisErrorDegrees = axisErrorDegrees;
            SensorYawAxis = sensorYawAxis;
            SensorPitchAxis = sensorPitchAxis;
            SensorRollAxis = sensorRollAxis;
        }

        public Quaternion SensorToRenderer { get; private set; }
        public float AxisErrorDegrees { get; private set; }
        public Vector3 SensorYawAxis { get; private set; }
        public Vector3 SensorPitchAxis { get; private set; }
        public Vector3 SensorRollAxis { get; private set; }
    }

    public static class PoseCalibration
    {
        public static bool TryCompute(
            IList<PoseSample> neutralSamples,
            IList<PoseSample> yawRightSamples,
            IList<PoseSample> yawLeftSamples,
            IList<PoseSample> pitchUpSamples,
            IList<PoseSample> pitchDownSamples,
            IList<PoseSample> rollRightSamples,
            IList<PoseSample> rollLeftSamples,
            out PoseCalibrationResult result,
            out string error)
        {
            result = null;
            error = null;
            Quaternion neutral;
            if (!TryAverage(neutralSamples, out neutral, out error) ||
                !HasSamples(yawRightSamples, "yaw-right", out error) ||
                !HasSamples(yawLeftSamples, "yaw-left", out error) ||
                !HasSamples(pitchUpSamples, "pitch-up", out error) ||
                !HasSamples(pitchDownSamples, "pitch-down", out error) ||
                !HasSamples(rollRightSamples, "roll-right", out error) ||
                !HasSamples(rollLeftSamples, "roll-left", out error))
            {
                return false;
            }

            Quaternion yawRight;
            Quaternion yawLeft;
            Quaternion pitchUp;
            Quaternion pitchDown;
            Quaternion rollRight;
            Quaternion rollLeft;
            if (!TryAverage(yawRightSamples, out yawRight, out error) ||
                !TryAverage(yawLeftSamples, out yawLeft, out error) ||
                !TryAverage(pitchUpSamples, out pitchUp, out error) ||
                !TryAverage(pitchDownSamples, out pitchDown, out error) ||
                !TryAverage(rollRightSamples, out rollRight, out error) ||
                !TryAverage(rollLeftSamples, out rollLeft, out error))
            {
                return false;
            }

            Quaternion inverseNeutral = Quaternion.Inverse(neutral);
            Vector3 sensorYawAxis;
            Vector3 sensorPitchAxis;
            Vector3 sensorRollAxis;
            if (!TrySignedAxis(
                    Quaternion.Multiply(inverseNeutral, yawRight),
                    Quaternion.Multiply(inverseNeutral, yawLeft),
                    out sensorYawAxis) ||
                !TrySignedAxis(
                    Quaternion.Multiply(inverseNeutral, pitchUp),
                    Quaternion.Multiply(inverseNeutral, pitchDown),
                    out sensorPitchAxis) ||
                !TrySignedAxis(
                    Quaternion.Multiply(inverseNeutral, rollRight),
                    Quaternion.Multiply(inverseNeutral, rollLeft),
                    out sensorRollAxis))
            {
                error = "Calibration movements were too small or ambiguous. Repeat each movement farther and hold it still before recording.";
                return false;
            }

            sensorYawAxis = Vector3.Normalize(sensorYawAxis);
            sensorPitchAxis = NormalizePerpendicular(sensorPitchAxis, sensorYawAxis);
            sensorRollAxis = NormalizePerpendicular(sensorRollAxis, sensorYawAxis, sensorPitchAxis);
            Quaternion sensorToRenderer = BuildBasisRotation(sensorPitchAxis, sensorYawAxis);
            Vector3 mappedYawAxis = Vector3.Transform(sensorYawAxis, sensorToRenderer);
            Vector3 mappedPitchAxis = Vector3.Transform(sensorPitchAxis, sensorToRenderer);
            Vector3 mappedRollAxis = Vector3.Transform(sensorRollAxis, sensorToRenderer);
            float axisErrorDegrees = Math.Max(
                VectorAngleDegrees(mappedYawAxis, Vector3.UnitY),
                Math.Max(
                    VectorAngleDegrees(mappedPitchAxis, Vector3.UnitX),
                    VectorAngleDegrees(mappedRollAxis, Vector3.UnitZ)));

            result = new PoseCalibrationResult(
                sensorToRenderer,
                axisErrorDegrees,
                sensorYawAxis,
                sensorPitchAxis,
                sensorRollAxis);
            return true;
        }

        private static bool TryAverage(IList<PoseSample> samples, out Quaternion orientation, out string error)
        {
            orientation = Quaternion.Identity;
            error = null;
            if (samples == null || samples.Count == 0)
            {
                error = "Calibration requires at least one sample for every movement.";
                return false;
            }

            Quaternion reference = samples[0].Orientation;
            Vector4 sum = Vector4.Zero;
            for (int index = 0; index < samples.Count; index++)
            {
                Quaternion sample = samples[index].Orientation;
                if (Quaternion.Dot(reference, sample) < 0.0f)
                {
                    sample = new Quaternion(-sample.X, -sample.Y, -sample.Z, -sample.W);
                }

                sum += new Vector4(sample.X, sample.Y, sample.Z, sample.W);
            }

            Quaternion averaged = new Quaternion(sum.X, sum.Y, sum.Z, sum.W);
            if (!PoseMath.TryNormalize(averaged, out orientation))
            {
                error = "Calibration samples could not be averaged into a valid orientation.";
                return false;
            }

            return true;
        }

        private static bool HasSamples(IList<PoseSample> samples, string movementName, out string error)
        {
            if (samples == null || samples.Count == 0)
            {
                error = "No samples were recorded for " + movementName + ".";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TrySignedAxis(Quaternion positive, Quaternion negative, out Vector3 axis)
        {
            Vector3 positiveVector = ToRotationVector(positive);
            Vector3 negativeVector = ToRotationVector(negative);
            Vector3 difference = positiveVector - negativeVector;
            if (difference.LengthSquared() <= 0.0001f)
            {
                axis = Vector3.Zero;
                return false;
            }

            axis = Vector3.Normalize(difference);
            return true;
        }

        private static Vector3 ToRotationVector(Quaternion orientation)
        {
            Quaternion normalized = PoseMath.Normalize(orientation);
            if (normalized.W < 0.0f)
            {
                normalized = new Quaternion(-normalized.X, -normalized.Y, -normalized.Z, -normalized.W);
            }

            float w = Math.Max(-1.0f, Math.Min(1.0f, normalized.W));
            float angle = 2.0f * (float)Math.Acos(w);
            float sineHalfAngle = (float)Math.Sqrt(Math.Max(0.0, 1.0 - w * w));
            if (sineHalfAngle <= 0.0001f || angle <= 0.0001f)
            {
                return Vector3.Zero;
            }

            return new Vector3(normalized.X, normalized.Y, normalized.Z) * (angle / sineHalfAngle);
        }

        private static Vector3 NormalizePerpendicular(Vector3 value, Vector3 firstAxis)
        {
            Vector3 perpendicular = value - firstAxis * Vector3.Dot(value, firstAxis);
            if (perpendicular.LengthSquared() <= 0.0001f)
            {
                throw new InvalidOperationException("Calibration movements did not produce independent axes.");
            }

            return Vector3.Normalize(perpendicular);
        }

        private static Vector3 NormalizePerpendicular(Vector3 value, Vector3 firstAxis, Vector3 secondAxis)
        {
            Vector3 perpendicular = value - firstAxis * Vector3.Dot(value, firstAxis) - secondAxis * Vector3.Dot(value, secondAxis);
            if (perpendicular.LengthSquared() <= 0.0001f)
            {
                throw new InvalidOperationException("Calibration movements did not produce three independent axes.");
            }

            return Vector3.Normalize(perpendicular);
        }

        private static Quaternion BuildBasisRotation(Vector3 sensorPitchAxis, Vector3 sensorYawAxis)
        {
            Quaternion yawAlignment = RotationBetween(sensorYawAxis, Vector3.UnitY);
            Vector3 pitchAfterYaw = Vector3.Transform(sensorPitchAxis, yawAlignment);
            Vector3 pitchProjection = NormalizePerpendicular(pitchAfterYaw, Vector3.UnitY);
            float sine = Vector3.Dot(Vector3.UnitY, Vector3.Cross(pitchProjection, Vector3.UnitX));
            float cosine = Vector3.Dot(pitchProjection, Vector3.UnitX);
            Quaternion pitchAlignment = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.Atan2(sine, cosine));
            return PoseMath.Normalize(Quaternion.Multiply(pitchAlignment, yawAlignment));
        }

        private static Quaternion RotationBetween(Vector3 from, Vector3 to)
        {
            Vector3 normalizedFrom = Vector3.Normalize(from);
            Vector3 normalizedTo = Vector3.Normalize(to);
            float dot = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(normalizedFrom, normalizedTo)));
            if (dot > 0.9999f)
            {
                return Quaternion.Identity;
            }

            if (dot < -0.9999f)
            {
                Vector3 axis = Vector3.Cross(normalizedFrom, Vector3.UnitX);
                if (axis.LengthSquared() <= 0.0001f)
                {
                    axis = Vector3.Cross(normalizedFrom, Vector3.UnitY);
                }

                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.PI);
            }

            return PoseMath.Normalize(new Quaternion(Vector3.Cross(normalizedFrom, normalizedTo), 1.0f + dot));
        }

        private static float VectorAngleDegrees(Vector3 from, Vector3 to)
        {
            float dot = Math.Max(-1.0f, Math.Min(1.0f, Vector3.Dot(Vector3.Normalize(from), Vector3.Normalize(to))));
            return 180.0f * (float)Math.Acos(dot) / (float)Math.PI;
        }
    }
}