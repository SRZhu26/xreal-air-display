using System;
using System.Diagnostics;

namespace PhoenixAirViewer.Platform
{
    public sealed class DesktopViewerFrameScheduler
    {
        private readonly long _minimumFrameIntervalTicks;
        private long _lastRenderTicks;
        private bool _hasRendered;
        private bool _hasLatestFrame;

        public DesktopViewerFrameScheduler(double targetFramesPerSecond)
        {
            if (double.IsNaN(targetFramesPerSecond) || double.IsInfinity(targetFramesPerSecond) || targetFramesPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException("targetFramesPerSecond");
            }

            _minimumFrameIntervalTicks = Math.Max(1L, (long)Math.Ceiling(Stopwatch.Frequency / targetFramesPerSecond));
        }

        public bool HasLatestFrame
        {
            get { return _hasLatestFrame; }
        }

        public long MinimumFrameIntervalTicks
        {
            get { return _minimumFrameIntervalTicks; }
        }

        public bool TrySchedule(DesktopCaptureStatus status, long nowTicks)
        {
            if (status == DesktopCaptureStatus.FrameReady)
            {
                _hasLatestFrame = true;
            }

            if (!_hasLatestFrame || (status != DesktopCaptureStatus.FrameReady && status != DesktopCaptureStatus.Timeout))
            {
                return false;
            }

            return TryScheduleLatestFrame(nowTicks);
        }

        public bool TryScheduleLatestFrame(long nowTicks)
        {
            if (!_hasLatestFrame)
            {
                return false;
            }

            if (_hasRendered && nowTicks - _lastRenderTicks < _minimumFrameIntervalTicks)
            {
                return false;
            }

            _lastRenderTicks = nowTicks;
            _hasRendered = true;
            return true;
        }

        public int GetWaitMilliseconds(long nowTicks)
        {
            if (!_hasRendered)
            {
                return 0;
            }

            long elapsedTicks = nowTicks - _lastRenderTicks;
            long remainingTicks = _minimumFrameIntervalTicks - elapsedTicks;
            if (remainingTicks <= 0)
            {
                return 0;
            }

            double milliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            return Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(milliseconds)));
        }
    }
}