using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace PhoenixAirViewer.Core
{
    public sealed class PoseEvidenceTarget
    {
        public PoseEvidenceTarget(string label, string targetAxis, float targetAngleDegrees)
        {
            Label = label;
            TargetAxis = targetAxis;
            TargetAngleDegrees = targetAngleDegrees;
        }

        public string Label { get; private set; }
        public string TargetAxis { get; private set; }
        public float TargetAngleDegrees { get; private set; }
    }

    public static class PoseEvidenceTargets
    {
        public static IList<PoseEvidenceTarget> CreateDefault()
        {
            return new List<PoseEvidenceTarget>
            {
                new PoseEvidenceTarget("neutral", "none", 0.0f),
                new PoseEvidenceTarget("yaw-left", "yaw", 30.0f),
                new PoseEvidenceTarget("yaw-right", "yaw", 30.0f),
                new PoseEvidenceTarget("pitch-up", "pitch", 20.0f),
                new PoseEvidenceTarget("pitch-down", "pitch", 20.0f),
                new PoseEvidenceTarget("roll-left", "roll", 20.0f),
                new PoseEvidenceTarget("roll-right", "roll", 20.0f)
            };
        }
    }

    public sealed class PoseEvidenceQuaternion
    {
        public PoseEvidenceQuaternion()
        {
        }

        public PoseEvidenceQuaternion(Quaternion value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = value.W;
        }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }

    public sealed class PoseEvidenceVector4
    {
        public PoseEvidenceVector4()
        {
        }

        public PoseEvidenceVector4(Vector4 value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
            W = value.W;
        }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }

    public sealed class PoseEvidencePose
    {
        public string Status { get; set; }
        public long SampleTimestampTicks { get; set; }
        public double AgeMilliseconds { get; set; }
        public bool HasNativeComponents { get; set; }
        public PoseEvidenceVector4 NativeComponents { get; set; }
        public PoseEvidenceQuaternion DecodedQuaternion { get; set; }
        public PoseEvidenceQuaternion MappedQuaternion { get; set; }
        public PoseEvidenceQuaternion RelativeQuaternion { get; set; }
    }

    public sealed class PoseEvidenceDisplay
    {
        public string Role { get; set; }
        public string DeviceName { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class PoseEvidencePresentation
    {
        public long PresentedTimestampTicks { get; set; }
        public long PoseSampleTimestampTicks { get; set; }
        public PoseEvidenceQuaternion ProcessedQuaternion { get; set; }
        public PoseEvidenceVector4 WorldOffset { get; set; }
        public long CaptureTimestampTicks { get; set; }
        public uint FrameWidth { get; set; }
        public uint FrameHeight { get; set; }
        public long PresentationCount { get; set; }
        public string SourceDisplayName { get; set; }
        public string OutputDisplayName { get; set; }
        public string CameraMode { get; set; }
    }

    public sealed class PoseEvidenceScreenshot
    {
        public string Role { get; set; }
        public string RelativePath { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }

    public sealed class PoseEvidenceRecord
    {
        public int SchemaVersion { get; set; }
        public string EvidenceId { get; set; }
        public int Sequence { get; set; }
        public string Label { get; set; }
        public string TargetAxis { get; set; }
        public float TargetAngleDegrees { get; set; }
        public float PitchSensitivity { get; set; }
        public float YawSensitivity { get; set; }
        public float RollSensitivity { get; set; }
        public float TranslationSensitivity { get; set; }
        public float PitchDriftRateDegreesPerSecond { get; set; }
        public float YawDriftRateDegreesPerSecond { get; set; }
        public string ActiveDistanceProfile { get; set; }
        public DateTime PressedUtc { get; set; }
        public long PressedMonotonicTicks { get; set; }
        public PoseEvidencePose PoseAtPress { get; set; }
        public PoseEvidencePose PoseAtScreenshot { get; set; }
        public PoseEvidencePresentation PoseUsedForLastPresentation { get; set; }
        public string ConnectionState { get; set; }
        public string LastError { get; set; }
        public string CameraMode { get; set; }
        public string SourceDisplayName { get; set; }
        public string OutputDisplayName { get; set; }
        public IList<PoseEvidenceDisplay> Displays { get; set; }
        public DateTime CaptureDueUtc { get; set; }
        public DateTime CapturedUtc { get; set; }
        public long CaptureMonotonicTicks { get; set; }
        public IList<PoseEvidenceScreenshot> Screenshots { get; set; }
        public string CaptureStatus { get; set; }
    }

    public sealed class PoseEvidenceManifest
    {
        public int SchemaVersion { get; set; }
        public string SessionId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public int ProcessId { get; set; }
        public string Runtime { get; set; }
        public string ProcessArchitecture { get; set; }
        public long StopwatchFrequency { get; set; }
        public string NativeQuaternionLayout { get; set; }
        public PoseEvidenceQuaternion SensorToRenderer { get; set; }
        public float PitchSensitivity { get; set; }
        public float YawSensitivity { get; set; }
        public float RollSensitivity { get; set; }
        public float TranslationSensitivity { get; set; }
        public float PitchDriftRateDegreesPerSecond { get; set; }
        public float YawDriftRateDegreesPerSecond { get; set; }
        public string ActiveDistanceProfile { get; set; }
        public string CameraMode { get; set; }
        public int CaptureDelayMilliseconds { get; set; }
        public PanelSettings Panel { get; set; }
        public IList<PoseEvidenceDisplay> Displays { get; set; }
    }

    public sealed class PoseEvidenceSessionWriter : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
        private readonly object _sync = new object();
        private readonly string _evidencePath;
        private bool _disposed;

        public PoseEvidenceSessionWriter(string sessionDirectory, PoseEvidenceManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
            {
                throw new ArgumentException("A diagnostic session directory is required.", "sessionDirectory");
            }

            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            Directory.CreateDirectory(sessionDirectory);
            SessionDirectory = sessionDirectory;
            _evidencePath = Path.Combine(sessionDirectory, "evidence.jsonl");
            File.WriteAllText(
                Path.Combine(sessionDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, SerializerOptions),
                new UTF8Encoding(false));
        }

        public string SessionDirectory { get; private set; }

        public void Write(PoseEvidenceRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException("record");
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                using (StreamWriter writer = new StreamWriter(new FileStream(_evidencePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
                {
                    writer.WriteLine(JsonSerializer.Serialize(record, SerializerOptions));
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
            }
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = false };
            options.Converters.Add(new QuaternionJsonConverter());
            return options;
        }
    }

    public static class PoseEvidenceFactory
    {
        public static PoseEvidencePose CreatePose(
            PoseObservation observation,
            Quaternion sensorToRenderer,
            bool hasNeutral,
            Quaternion neutral,
            long nowTicks,
            string status)
        {
            if (observation == null)
            {
                return new PoseEvidencePose
                {
                    Status = status,
                    AgeMilliseconds = -1.0
                };
            }

            Quaternion mapped = PoseMath.MapBasis(observation.Orientation, sensorToRenderer);
            Quaternion relative = Quaternion.Identity;
            if (hasNeutral)
            {
                relative = PoseMath.Normalize(Quaternion.Multiply(Quaternion.Inverse(neutral), mapped));
            }

            return new PoseEvidencePose
            {
                Status = status,
                SampleTimestampTicks = observation.TimestampTicks,
                AgeMilliseconds = Math.Max(0.0, PoseClock.SecondsBetween(observation.TimestampTicks, nowTicks) * 1000.0),
                HasNativeComponents = observation.HasNativeComponents,
                NativeComponents = observation.HasNativeComponents ? new PoseEvidenceVector4(observation.NativeComponents) : null,
                DecodedQuaternion = new PoseEvidenceQuaternion(observation.Orientation),
                MappedQuaternion = new PoseEvidenceQuaternion(mapped),
                RelativeQuaternion = hasNeutral ? new PoseEvidenceQuaternion(relative) : null
            };
        }

        public static PoseEvidencePresentation CreatePresentation(PosePresentationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            return new PoseEvidencePresentation
            {
                PresentedTimestampTicks = snapshot.PresentedTimestampTicks,
                PoseSampleTimestampTicks = snapshot.PoseSampleTimestampTicks,
                ProcessedQuaternion = new PoseEvidenceQuaternion(snapshot.ProcessedOrientation),
                WorldOffset = new PoseEvidenceVector4(new Vector4(snapshot.WorldOffset, 0.0f)),
                CaptureTimestampTicks = snapshot.CaptureTimestampTicks,
                FrameWidth = snapshot.FrameWidth,
                FrameHeight = snapshot.FrameHeight,
                PresentationCount = snapshot.PresentationCount,
                SourceDisplayName = snapshot.SourceDisplayName,
                OutputDisplayName = snapshot.OutputDisplayName,
                CameraMode = snapshot.CameraMode
            };
        }
    }
}