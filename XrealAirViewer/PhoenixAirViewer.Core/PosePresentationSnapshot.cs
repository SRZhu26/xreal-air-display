using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePresentationSnapshot
    {
        public PosePresentationSnapshot(
            long presentedTimestampTicks,
            long poseSampleTimestampTicks,
            Quaternion processedOrientation,
            long captureTimestampTicks,
            uint frameWidth,
            uint frameHeight,
            long presentationCount,
            string sourceDisplayName,
            string outputDisplayName,
            string cameraMode)
            : this(
                presentedTimestampTicks,
                poseSampleTimestampTicks,
                processedOrientation,
                captureTimestampTicks,
                frameWidth,
                frameHeight,
                presentationCount,
                sourceDisplayName,
                outputDisplayName,
                cameraMode,
                Vector3.Zero)
        {
        }

        public PosePresentationSnapshot(
            long presentedTimestampTicks,
            long poseSampleTimestampTicks,
            Quaternion processedOrientation,
            long captureTimestampTicks,
            uint frameWidth,
            uint frameHeight,
            long presentationCount,
            string sourceDisplayName,
            string outputDisplayName,
            string cameraMode,
            Vector3 worldOffset)
        {
            PresentedTimestampTicks = presentedTimestampTicks;
            PoseSampleTimestampTicks = poseSampleTimestampTicks;
            ProcessedOrientation = processedOrientation;
            CaptureTimestampTicks = captureTimestampTicks;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            PresentationCount = presentationCount;
            SourceDisplayName = sourceDisplayName;
            OutputDisplayName = outputDisplayName;
            CameraMode = cameraMode;
            WorldOffset = worldOffset;
        }

        public long PresentedTimestampTicks { get; private set; }
        public long PoseSampleTimestampTicks { get; private set; }
        public Quaternion ProcessedOrientation { get; private set; }
        public long CaptureTimestampTicks { get; private set; }
        public uint FrameWidth { get; private set; }
        public uint FrameHeight { get; private set; }
        public long PresentationCount { get; private set; }
        public string SourceDisplayName { get; private set; }
        public string OutputDisplayName { get; private set; }
        public string CameraMode { get; private set; }
        public Vector3 WorldOffset { get; private set; }
    }
}