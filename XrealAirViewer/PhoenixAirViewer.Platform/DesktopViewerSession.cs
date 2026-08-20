using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public sealed class DesktopViewerSession : IDisposable
    {
        private const int CapturePollIntervalMilliseconds = 33;
        private readonly DisplayInfo _sourceDisplay;
        private readonly DisplayInfo _outputDisplay;
        private readonly IntPtr _outputWindowHandle;
        private readonly uint _outputWidth;
        private readonly uint _outputHeight;
        private readonly IPoseSource _poseSource;
        private readonly LatestPoseStore _poseStore;
        private readonly PosePollingWorker _ownedPoseWorker;
        private readonly PosePipeline _posePipeline;
        private readonly IViewerLogger _logger;
        private PanelSettings _panelSettings;
        private int _panelSettingsVersion;
        private Vector3 _worldOffset;
        private bool _headFollowingPreview;
        private readonly object _sync = new object();
        private PosePresentationSnapshot _latestPresentation;
        private CancellationTokenSource _cancellation;
        private Thread _renderThread;
        private bool _disposed;

        public DesktopViewerSession(DisplayInfo sourceDisplay, DisplayInfo outputDisplay, IntPtr outputWindowHandle, uint outputWidth, uint outputHeight, IPoseSource poseSource, PosePipeline posePipeline)
            : this(sourceDisplay, outputDisplay, outputWindowHandle, outputWidth, outputHeight, poseSource, posePipeline, new PanelSettings(), null)
        {
        }

        public DesktopViewerSession(DisplayInfo sourceDisplay, DisplayInfo outputDisplay, IntPtr outputWindowHandle, uint outputWidth, uint outputHeight, IPoseSource poseSource, PosePipeline posePipeline, PanelSettings panelSettings)
            : this(sourceDisplay, outputDisplay, outputWindowHandle, outputWidth, outputHeight, poseSource, posePipeline, panelSettings, null)
        {
        }

        public DesktopViewerSession(DisplayInfo sourceDisplay, DisplayInfo outputDisplay, IntPtr outputWindowHandle, uint outputWidth, uint outputHeight, IPoseSource poseSource, PosePipeline posePipeline, PanelSettings panelSettings, IViewerLogger logger)
            : this(sourceDisplay, outputDisplay, outputWindowHandle, outputWidth, outputHeight, poseSource, posePipeline, panelSettings, logger, null)
        {
        }

        public DesktopViewerSession(DisplayInfo sourceDisplay, DisplayInfo outputDisplay, IntPtr outputWindowHandle, uint outputWidth, uint outputHeight, IPoseSource poseSource, PosePipeline posePipeline, PanelSettings panelSettings, IViewerLogger logger, LatestPoseStore poseStore)
        {
            if (sourceDisplay == null)
            {
                throw new ArgumentNullException("sourceDisplay");
            }

            if (outputDisplay == null)
            {
                throw new ArgumentNullException("outputDisplay");
            }

            if (outputWindowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("The output window must have a native handle.", "outputWindowHandle");
            }

            if (panelSettings == null)
            {
                throw new ArgumentNullException("panelSettings");
            }

            if (string.Equals(sourceDisplay.DeviceName, outputDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The source and output displays must be different.", "outputDisplay");
            }

            _sourceDisplay = sourceDisplay;
            _outputDisplay = outputDisplay;
            _outputWindowHandle = outputWindowHandle;
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
            _poseSource = poseSource;
            _poseStore = poseStore ?? (poseSource == null ? null : new LatestPoseStore());
            _ownedPoseWorker = poseStore == null && poseSource != null
                ? new PosePollingWorker(poseSource, _poseStore, _logger)
                : null;
            _posePipeline = posePipeline;
            _logger = logger ?? NullViewerLogger.Instance;
            panelSettings.Validate();
            _panelSettings = panelSettings.Clone();
            _panelSettingsVersion = 1;
        }

        public event Action<string> StatusChanged;

        public bool TryGetLatestPresentation(out PosePresentationSnapshot snapshot)
        {
            lock (_sync)
            {
                snapshot = _latestPresentation;
                return snapshot != null;
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _renderThread != null && _renderThread.IsAlive;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_renderThread != null)
                {
                    return;
                }

                _cancellation = new CancellationTokenSource();
                _logger.Information("session.start.request", "Desktop viewer session start requested.");
                if (_ownedPoseWorker != null)
                {
                    _ownedPoseWorker.Start();
                }

                _renderThread = new Thread(RenderLoop)
                {
                    IsBackground = true,
                    Name = "Phoenix Air desktop render"
                };
                _renderThread.Start();
            }
        }

        public void UpdatePanelSettings(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                throw new ArgumentNullException("panelSettings");
            }

            panelSettings.Validate();
            lock (_sync)
            {
                ThrowIfDisposed();
                _panelSettings = panelSettings.Clone();
                _panelSettingsVersion++;
                _logger.Debug("session.panel.updated", "Panel settings update queued for the render thread.");
            }
        }

        public void UpdatePresentationTransform(Vector3 worldOffset, bool headFollowingPreview)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _worldOffset = worldOffset;
                _headFollowingPreview = headFollowingPreview;
            }
        }

        public void Dispose()
        {
            Thread renderThread;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                }
                renderThread = _renderThread;
            }

            if (renderThread != null && renderThread != Thread.CurrentThread)
            {
                renderThread.Join(3000);
            }

            if (_ownedPoseWorker != null)
            {
                _ownedPoseWorker.Dispose();
            }

            lock (_sync)
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                _renderThread = null;
            }
        }

        private void RenderLoop()
        {
            CancellationToken cancellation = _cancellation.Token;
            int retryDelayMilliseconds = 250;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    bool shouldRetry = RunRenderAttempt(cancellation);
                    if (!shouldRetry || cancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    Report("recovering; retry in " + retryDelayMilliseconds + " ms");
                    if (cancellation.WaitHandle.WaitOne(retryDelayMilliseconds))
                    {
                        break;
                    }

                    retryDelayMilliseconds = Math.Min(retryDelayMilliseconds * 2, 5000);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("session.exception", "Desktop viewer session stopped because of an exception.", exception);
                Report("stopped: " + exception.Message);
            }
            finally
            {
                lock (_sync)
                {
                    _renderThread = null;
                }
            }
        }

        private bool RunRenderAttempt(CancellationToken cancellation)
        {
            try
            {
                int appliedPanelSettingsVersion;
                PanelSettings initialPanelSettings = GetPanelSettingsSnapshot(out appliedPanelSettingsVersion);
                using (DesktopDuplicationCapture capture = new DesktopDuplicationCapture(_sourceDisplay))
                {
                    long outputAdapterLuid;
                    if (!DisplayEnumerator.TryGetAdapterLuid(_outputDisplay.DeviceName, out outputAdapterLuid))
                    {
                        Report("stopped: the selected output display is not available through DXGI.");
                        return false;
                    }

                    if (capture.AdapterLuid != outputAdapterLuid)
                    {
                        string message = "Source and output displays use different graphics adapters; cross-adapter capture is not supported.";
                        _logger.Warning("session.adapter.mismatch", message);
                        Report("stopped: " + message);
                        return false;
                    }

                    using (D3D11PanelRenderer renderer = new D3D11PanelRenderer(capture, _outputWindowHandle, _outputWidth, _outputHeight, initialPanelSettings))
                    {
                        DesktopCaptureFrame latestFrame = null;
                        DesktopViewerFrameScheduler frameScheduler = new DesktopViewerFrameScheduler(60.0);
                        Quaternion latestOrientation = Quaternion.Identity;
                        bool hasValidOrientation = false;
                        long metricsStartTicks = PoseClock.NowTicks();
                        long renderedFrameCount = 0;
                        long capturedFrameCount = 0;
                        long captureTimeoutCount = 0;
                        long presentationSkipCount = 0;
                        double latestPoseAgeSeconds = -1.0;
                        double latestCaptureAgeSeconds = -1.0;
                        long latestPoseSampleTimestampTicks = 0;
                        long presentationCount = 0;
                        long lastPresentedTicks = 0;
                        double totalPresentationIntervalMilliseconds = 0.0;
                        double maximumPresentationIntervalMilliseconds = 0.0;
                        long presentationIntervalCount = 0;
                        PoseSample previousPoseSample = default(PoseSample);
                        bool hasPreviousPoseSample = false;
                        double maximumRawPoseVelocityDegreesPerSecond = 0.0;
                        Quaternion lastLoggedPoseOrientation = Quaternion.Identity;
                        bool hasLoggedPoseOrientation = false;
                        bool hasReportedPresent = false;
                        long nextCaptureTicks = 0;
                        Report("running on " + _sourceDisplay.DeviceName + " -> " + _outputDisplay.DeviceName);
                        while (!cancellation.IsCancellationRequested)
                        {
                            PanelSettings panelSettings = GetPanelSettingsSnapshot(out int panelSettingsVersion);
                            if (panelSettingsVersion != appliedPanelSettingsVersion)
                            {
                                renderer.UpdatePanelSettings(panelSettings);
                                appliedPanelSettingsVersion = panelSettingsVersion;
                            }

                            long nowTicks = PoseClock.NowTicks();
                            bool shouldPollCapture = nowTicks >= nextCaptureTicks;
                            DesktopCaptureResult result = null;
                            if (shouldPollCapture)
                            {
                                nextCaptureTicks = nowTicks + Math.Max(1L, Stopwatch.Frequency * CapturePollIntervalMilliseconds / 1000L);
                                result = capture.TryAcquire(0);
                                if (result.Status == DesktopCaptureStatus.FrameReady)
                                {
                                    latestFrame = result.Frame;
                                    capturedFrameCount++;
                                }
                                else if (result.Status == DesktopCaptureStatus.Timeout)
                                {
                                    captureTimeoutCount++;
                                }
                            }

                            nowTicks = PoseClock.NowTicks();
                            bool shouldRender = latestFrame != null && (shouldPollCapture
                                ? frameScheduler.TrySchedule(result.Status, nowTicks)
                                : frameScheduler.TryScheduleLatestFrame(nowTicks));
                            if (shouldRender)
                            {
                                Quaternion orientation = hasValidOrientation ? latestOrientation : Quaternion.Identity;
                                PoseSample sample;
                                if (_poseStore != null && _posePipeline != null && _poseStore.TryRead(out sample) && sample.AgeSeconds(PoseClock.NowTicks()) <= 0.5)
                                {
                                    _posePipeline.TryProcess(sample, out orientation);
                                    if (!hasPreviousPoseSample || sample.TimestampTicks != previousPoseSample.TimestampTicks)
                                    {
                                        if (hasPreviousPoseSample)
                                        {
                                            double sampleDeltaSeconds = PoseClock.SecondsBetween(previousPoseSample.TimestampTicks, sample.TimestampTicks);
                                            maximumRawPoseVelocityDegreesPerSecond = Math.Max(
                                                maximumRawPoseVelocityDegreesPerSecond,
                                                PoseMath.AngularVelocityDegreesPerSecond(
                                                    previousPoseSample.Orientation,
                                                    sample.Orientation,
                                                    sampleDeltaSeconds));
                                        }

                                        previousPoseSample = sample;
                                        hasPreviousPoseSample = true;
                                    }

                                    latestOrientation = orientation;
                                    hasValidOrientation = true;
                                    latestPoseSampleTimestampTicks = sample.TimestampTicks;
                                    latestPoseAgeSeconds = sample.AgeSeconds(PoseClock.NowTicks());
                                }
                                else if (_poseStore != null && _poseStore.TryRead(out sample))
                                {
                                    latestPoseAgeSeconds = sample.AgeSeconds(PoseClock.NowTicks());
                                }

                                Vector3 worldOffset;
                                bool headFollowingPreview;
                                GetPresentationTransformSnapshot(out worldOffset, out headFollowingPreview);
                                if (!headFollowingPreview)
                                {
                                    worldOffset += PanelViewTransform.CreateAngleTranslation(
                                        orientation,
                                        panelSettings.PanelDistanceMeters,
                                        panelSettings.TranslationSensitivity);
                                }
                                DesktopCursorState cursor = DesktopCursorState.Read(
                                    _sourceDisplay,
                                    latestFrame.Width,
                                    latestFrame.Height);
                                if (!renderer.Render(latestFrame, orientation, worldOffset, headFollowingPreview, cursor))
                                {
                                    presentationSkipCount++;
                                    continue;
                                }

                                long presentedTicks = PoseClock.NowTicks();
                                if (lastPresentedTicks != 0)
                                {
                                    double presentationIntervalMilliseconds = PoseClock.SecondsBetween(lastPresentedTicks, presentedTicks) * 1000.0;
                                    totalPresentationIntervalMilliseconds += presentationIntervalMilliseconds;
                                    maximumPresentationIntervalMilliseconds = Math.Max(
                                        maximumPresentationIntervalMilliseconds,
                                        presentationIntervalMilliseconds);
                                    presentationIntervalCount++;
                                }

                                lastPresentedTicks = presentedTicks;
                                renderedFrameCount++;
                                presentationCount++;
                                lock (_sync)
                                {
                                    _latestPresentation = new PosePresentationSnapshot(
                                        PoseClock.NowTicks(),
                                        latestPoseSampleTimestampTicks,
                                        orientation,
                                        latestFrame.TimestampTicks,
                                        (uint)latestFrame.Width,
                                        (uint)latestFrame.Height,
                                        presentationCount,
                                        _sourceDisplay.DeviceName,
                                        _outputDisplay.DeviceName,
                                        headFollowingPreview ? "head-following-preview" : "world-locked",
                                        worldOffset);
                                }
                                if (!hasReportedPresent)
                                {
                                    hasReportedPresent = true;
                                    Report(
                                        "presenting on " + _outputDisplay.DeviceName + "; frame=" + latestFrame.Width + "x" + latestFrame.Height + "; " +
                                        (capture.LastAcquiredSourcePixels ?? "sourcePixels=unavailable") + "; " +
                                        capture.DescribeLatestTexturePixels());
                                }
                                latestCaptureAgeSeconds = Math.Max(0.0, PoseClock.SecondsBetween(latestFrame.TimestampTicks, PoseClock.NowTicks()));
                                ReportMetricsIfDue(
                                    ref metricsStartTicks,
                                    ref renderedFrameCount,
                                    ref capturedFrameCount,
                                    ref captureTimeoutCount,
                                    ref presentationSkipCount,
                                    latestPoseAgeSeconds,
                                    latestCaptureAgeSeconds,
                                    latestPoseSampleTimestampTicks,
                                    presentationCount,
                                    ref maximumRawPoseVelocityDegreesPerSecond,
                                    ref totalPresentationIntervalMilliseconds,
                                    ref presentationIntervalCount,
                                    ref maximumPresentationIntervalMilliseconds,
                                    ref lastLoggedPoseOrientation,
                                    ref hasLoggedPoseOrientation,
                                    orientation,
                                    hasValidOrientation,
                                    worldOffset,
                                    headFollowingPreview);
                            }
                            else if (result == null || result.Status == DesktopCaptureStatus.FrameReady || result.Status == DesktopCaptureStatus.Timeout)
                            {
                                nowTicks = PoseClock.NowTicks();
                                int waitMilliseconds = frameScheduler.GetWaitMilliseconds(nowTicks);
                                if (nextCaptureTicks > nowTicks)
                                {
                                    double captureWaitMilliseconds = (nextCaptureTicks - nowTicks) * 1000.0 / Stopwatch.Frequency;
                                    waitMilliseconds = waitMilliseconds == 0
                                        ? Math.Max(1, (int)Math.Ceiling(captureWaitMilliseconds))
                                        : Math.Min(waitMilliseconds, Math.Max(1, (int)Math.Ceiling(captureWaitMilliseconds)));
                                }

                                if (waitMilliseconds > 0 && cancellation.WaitHandle.WaitOne(waitMilliseconds))
                                {
                                    break;
                                }

                                continue;
                            }
                            else if (result.Status == DesktopCaptureStatus.AccessLost || result.Status == DesktopCaptureStatus.DeviceRemoved)
                            {
                                Report("recovering: " + result.Status);
                                return true;
                            }
                            else
                            {
                                Report("recovering: " + (result.Error ?? result.Status.ToString()));
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception exception)
            {
                _logger.Error("session.attempt.failed", "A capture/render attempt failed.", exception);
                Report("recovering: " + exception.Message);
                return true;
            }
        }

        private void Report(string status)
        {
            if (status != null && status.StartsWith("stopped", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("session.status", status);
            }
            else
            {
                _logger.Information("session.status", status);
            }

            Action<string> handler = StatusChanged;
            if (handler != null)
            {
                handler(status);
            }
        }

        private void ReportMetricsIfDue(
            ref long metricsStartTicks,
            ref long renderedFrameCount,
            ref long capturedFrameCount,
            ref long captureTimeoutCount,
            ref long presentationSkipCount,
            double poseAgeSeconds,
            double captureAgeSeconds,
            long latestPoseSampleTimestampTicks,
            long presentationCount,
            ref double maximumRawPoseVelocityDegreesPerSecond,
            ref double totalPresentationIntervalMilliseconds,
            ref long presentationIntervalCount,
            ref double maximumPresentationIntervalMilliseconds,
            ref Quaternion lastLoggedPoseOrientation,
            ref bool hasLoggedPoseOrientation,
            Quaternion orientation,
            bool hasValidOrientation,
            Vector3 worldOffset,
            bool headFollowingPreview)
        {
            long nowTicks = PoseClock.NowTicks();
            double elapsedSeconds = PoseClock.SecondsBetween(metricsStartTicks, nowTicks);
            if (elapsedSeconds < 1.0)
            {
                return;
            }

            double renderFps = renderedFrameCount / elapsedSeconds;
            double averagePresentationIntervalMilliseconds = presentationIntervalCount == 0
                ? 0.0
                : totalPresentationIntervalMilliseconds / presentationIntervalCount;
            PosePipelineSettings poseSettings = _posePipeline == null ? null : _posePipeline.Settings;
            string poseDiagnostics = "poseX=n/a; poseY=n/a; poseZ=n/a; poseW=n/a; poseDeltaDeg=n/a";
            if (hasValidOrientation)
            {
                double poseDeltaDegrees = hasLoggedPoseOrientation
                    ? PoseMath.AngularDistanceRadians(lastLoggedPoseOrientation, orientation) * 180.0 / Math.PI
                    : 0.0;
                poseDiagnostics = string.Format(
                    "poseX={0:0.000}; poseY={1:0.000}; poseZ={2:0.000}; poseW={3:0.000}; poseDeltaDeg={4:0.0}",
                    orientation.X,
                    orientation.Y,
                    orientation.Z,
                    orientation.W,
                    poseDeltaDegrees);
                lastLoggedPoseOrientation = orientation;
                hasLoggedPoseOrientation = true;
            }

            _logger.Information(
                "session.metrics",
                string.Format(
                    "renderFps={0:0.0}; rendered={1}; captured={2}; timeouts={3}; presentSkips={4}; poseAgeMs={5}; captureAgeMs={6}; {7}",
                    renderFps,
                    renderedFrameCount,
                    capturedFrameCount,
                    captureTimeoutCount,
                    presentationSkipCount,
                    poseAgeSeconds < 0.0 ? "n/a" : (poseAgeSeconds * 1000.0).ToString("0.0"),
                    captureAgeSeconds < 0.0 ? "n/a" : (captureAgeSeconds * 1000.0).ToString("0.0"),
                    "lastPresentedPoseTs=" + (latestPoseSampleTimestampTicks == 0 ? "n/a" : latestPoseSampleTimestampTicks.ToString()) +
                    "; presentationCount=" + presentationCount +
                    "; poseMaxVelocityDegPerSec=" + maximumRawPoseVelocityDegreesPerSecond.ToString("0.0") +
                    "; presentIntervalMs=" + averagePresentationIntervalMilliseconds.ToString("0.0") + "/" + maximumPresentationIntervalMilliseconds.ToString("0.0") +
                    "; cameraMode=" + (headFollowingPreview ? "head-following-preview" : "world-locked") +
                    "; worldOffset=" + worldOffset.X.ToString("0.000") + "," + worldOffset.Y.ToString("0.000") + "," + worldOffset.Z.ToString("0.000") +
                    "; pitchDriftRate=" + (poseSettings == null ? "n/a" : poseSettings.PitchDriftRateDegreesPerSecond.ToString("0.00") + "deg/s") +
                    "; yawDriftRate=" + (poseSettings == null ? "n/a" : poseSettings.YawDriftRateDegreesPerSecond.ToString("0.00") + "deg/s") +
                    "; " + poseDiagnostics));
            metricsStartTicks = nowTicks;
            renderedFrameCount = 0;
            capturedFrameCount = 0;
            captureTimeoutCount = 0;
            presentationSkipCount = 0;
            maximumRawPoseVelocityDegreesPerSecond = 0.0;
            totalPresentationIntervalMilliseconds = 0.0;
            presentationIntervalCount = 0;
            maximumPresentationIntervalMilliseconds = 0.0;
        }

        private PanelSettings GetPanelSettingsSnapshot(out int version)
        {
            lock (_sync)
            {
                version = _panelSettingsVersion;
                return _panelSettings.Clone();
            }
        }

        private void GetPresentationTransformSnapshot(out Vector3 worldOffset, out bool headFollowingPreview)
        {
            lock (_sync)
            {
                worldOffset = _worldOffset;
                headFollowingPreview = _headFollowingPreview;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
