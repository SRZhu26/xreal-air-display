using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public sealed class DesktopViewerSession : IDisposable
    {
        private readonly DisplayInfo _sourceDisplay;
        private readonly DisplayInfo _outputDisplay;
        private readonly IntPtr _outputWindowHandle;
        private readonly uint _outputWidth;
        private readonly uint _outputHeight;
        private readonly IPoseSource _poseSource;
        private readonly PosePipeline _posePipeline;
        private readonly IViewerLogger _logger;
        private PanelSettings _panelSettings;
        private int _panelSettingsVersion;
        private readonly object _sync = new object();
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
            _posePipeline = posePipeline;
            _logger = logger ?? NullViewerLogger.Instance;
            panelSettings.Validate();
            _panelSettings = panelSettings.Clone();
            _panelSettingsVersion = 1;
        }

        public event Action<string> StatusChanged;

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
                using (D3D11PanelRenderer renderer = new D3D11PanelRenderer(capture, _outputWindowHandle, _outputWidth, _outputHeight, initialPanelSettings))
                {
                    DesktopCaptureFrame latestFrame = null;
                    long metricsStartTicks = PoseClock.NowTicks();
                    long renderedFrameCount = 0;
                    long capturedFrameCount = 0;
                    long captureTimeoutCount = 0;
                    double latestPoseAgeSeconds = -1.0;
                    double latestCaptureAgeSeconds = -1.0;
                    Report("running on " + _sourceDisplay.DeviceName + " -> " + _outputDisplay.DeviceName);
                    while (!cancellation.IsCancellationRequested)
                    {
                        PanelSettings panelSettings = GetPanelSettingsSnapshot(out int panelSettingsVersion);
                        if (panelSettingsVersion != appliedPanelSettingsVersion)
                        {
                            renderer.UpdatePanelSettings(panelSettings);
                            appliedPanelSettingsVersion = panelSettingsVersion;
                        }

                        DesktopCaptureResult result = capture.TryAcquire(16);
                        if (result.Status == DesktopCaptureStatus.FrameReady)
                        {
                            latestFrame = result.Frame;
                            capturedFrameCount++;
                        }
                        else if (result.Status == DesktopCaptureStatus.Timeout)
                        {
                            captureTimeoutCount++;
                        }

                        if (latestFrame != null && (result.Status == DesktopCaptureStatus.FrameReady || result.Status == DesktopCaptureStatus.Timeout))
                        {
                            Quaternion orientation = Quaternion.Identity;
                            PoseSample sample;
                            if (_poseSource != null && _posePipeline != null && _poseSource.TryGetLatest(out sample))
                            {
                                _posePipeline.TryProcess(sample, out orientation);
                                latestPoseAgeSeconds = sample.AgeSeconds(PoseClock.NowTicks());
                            }
                            renderer.Render(latestFrame, orientation);
                            renderedFrameCount++;
                            latestCaptureAgeSeconds = Math.Max(0.0, PoseClock.SecondsBetween(latestFrame.TimestampTicks, PoseClock.NowTicks()));
                            ReportMetricsIfDue(
                                ref metricsStartTicks,
                                ref renderedFrameCount,
                                ref capturedFrameCount,
                                ref captureTimeoutCount,
                                latestPoseAgeSeconds,
                                latestCaptureAgeSeconds);
                        }
                        else if (result.Status == DesktopCaptureStatus.Timeout)
                        {
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
            double poseAgeSeconds,
            double captureAgeSeconds)
        {
            long nowTicks = PoseClock.NowTicks();
            double elapsedSeconds = PoseClock.SecondsBetween(metricsStartTicks, nowTicks);
            if (elapsedSeconds < 1.0)
            {
                return;
            }

            double renderFps = renderedFrameCount / elapsedSeconds;
            _logger.Information(
                "session.metrics",
                string.Format(
                    "renderFps={0:0.0}; rendered={1}; captured={2}; timeouts={3}; poseAgeMs={4}; captureAgeMs={5}",
                    renderFps,
                    renderedFrameCount,
                    capturedFrameCount,
                    captureTimeoutCount,
                    poseAgeSeconds < 0.0 ? "n/a" : (poseAgeSeconds * 1000.0).ToString("0.0"),
                    captureAgeSeconds < 0.0 ? "n/a" : (captureAgeSeconds * 1000.0).ToString("0.0")));
            metricsStartTicks = nowTicks;
            renderedFrameCount = 0;
            capturedFrameCount = 0;
            captureTimeoutCount = 0;
        }

        private PanelSettings GetPanelSettingsSnapshot(out int version)
        {
            lock (_sync)
            {
                version = _panelSettingsVersion;
                return _panelSettings.Clone();
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
