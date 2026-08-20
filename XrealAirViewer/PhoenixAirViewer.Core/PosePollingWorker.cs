using System;
using System.Threading;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePollingWorker : IDisposable
    {
        private const int PollIntervalMilliseconds = 5;
        private readonly IPoseSource _source;
        private readonly LatestPoseStore _store;
        private readonly LatestPoseObservationStore _observationStore;
        private readonly IViewerLogger _logger;
        private readonly bool _manageConnection;
        private readonly object _sync = new object();
        private CancellationTokenSource _cancellation;
        private Thread _thread;
        private bool _disposed;
        private bool _connected;
        private string _lastError;
        private string _lastReportedError;
        private long _lastSuccessfulSampleTicks;

        public PosePollingWorker(IPoseSource source, LatestPoseStore store, IViewerLogger logger)
            : this(source, store, null, logger, true)
        {
        }

        public PosePollingWorker(IPoseSource source, LatestPoseStore store, IViewerLogger logger, bool manageConnection)
            : this(source, store, null, logger, manageConnection)
        {
        }

        public PosePollingWorker(IPoseSource source, LatestPoseStore store, LatestPoseObservationStore observationStore, IViewerLogger logger, bool manageConnection)
        {
            _source = source ?? throw new ArgumentNullException("source");
            _store = store ?? throw new ArgumentNullException("store");
            _observationStore = observationStore;
            _logger = logger ?? NullViewerLogger.Instance;
            _manageConnection = manageConnection;
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _thread != null && _thread.IsAlive;
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                {
                    return _connected;
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_sync)
                {
                    return _lastError;
                }
            }
        }

        public long LastSuccessfulSampleTicks
        {
            get
            {
                lock (_sync)
                {
                    return _lastSuccessfulSampleTicks;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                if (_thread != null)
                {
                    return;
                }

                _cancellation = new CancellationTokenSource();
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "Phoenix Air pose polling"
                };
                if (OperatingSystem.IsWindows())
                {
                    _thread.SetApartmentState(ApartmentState.STA);
                }
                _thread.Start();
            }
        }

        public bool Stop(int timeoutMilliseconds)
        {
            Thread thread;
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                thread = _thread;
                cancellation = _cancellation;
                if (cancellation != null)
                {
                    cancellation.Cancel();
                }
            }

            bool stopped = thread == null || thread == Thread.CurrentThread || thread.Join(timeoutMilliseconds);
            if (stopped)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_thread, thread) || thread == null || (thread != Thread.CurrentThread && !thread.IsAlive))
                    {
                        _thread = null;
                    }

                    if (cancellation != null && ReferenceEquals(_cancellation, cancellation) && thread != Thread.CurrentThread)
                    {
                        _cancellation.Dispose();
                        _cancellation = null;
                    }
                }
            }

            return stopped;
        }

        public void Dispose()
        {
            bool stopped = Stop(3000);
            lock (_sync)
            {
                _disposed = true;
            }

            if (!stopped)
            {
                _logger.Warning("pose.worker.stop.timeout", "Pose polling did not stop within the shutdown timeout.");
            }
        }

        private void Run()
        {
            CancellationToken cancellation;
            lock (_sync)
            {
                cancellation = _cancellation.Token;
            }

            int failureCount = 0;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        if (!_manageConnection)
                        {
                            if (!_source.IsConnected)
                            {
                                failureCount++;
                                SetFailure(false, "The pose source is not connected.");
                                if (Wait(cancellation, FailureDelayMilliseconds(failureCount)))
                                {
                                    break;
                                }

                                continue;
                            }

                            SetConnected();
                            failureCount = 0;
                        }

                        string error;
                        if (!_source.TryConnect(out error))
                        {
                            failureCount++;
                            SetFailure(false, error ?? "The pose source could not connect.");
                            if (Wait(cancellation, FailureDelayMilliseconds(failureCount)))
                            {
                                break;
                            }

                            continue;
                        }

                        SetConnected();
                        failureCount = 0;
                    }

                    PoseSample sample;
                    if (TryReadPose(out sample))
                    {
                        _store.Publish(sample);
                        SetSampleReceived(sample);
                        failureCount = 0;
                        if (Wait(cancellation, PollIntervalMilliseconds))
                        {
                            break;
                        }
                    }
                    else
                    {
                        failureCount++;
                        string error = _source.LastError ?? "The pose source returned no sample.";
                        SetFailure(_source.IsConnected, error);
                        if (failureCount >= 3 && _source.IsConnected && _manageConnection)
                        {
                            try
                            {
                                _source.Disconnect();
                            }
                            catch (Exception exception)
                            {
                                _logger.Warning("pose.worker.disconnect.failed", exception.Message);
                            }

                            SetFailure(false, error);
                        }

                        if (Wait(cancellation, FailureDelayMilliseconds(failureCount)))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                SetFailure(false, exception.Message);
                _logger.Error("pose.worker.exception", "Pose polling stopped because of an exception.", exception);
            }
            finally
            {
                try
                {
                    if (_manageConnection)
                    {
                        _source.Disconnect();
                    }
                }
                catch (Exception exception)
                {
                    _logger.Error("pose.worker.disconnect.failed", "The pose source could not disconnect cleanly.", exception);
                }

                lock (_sync)
                {
                    _connected = false;
                    if (ReferenceEquals(_thread, Thread.CurrentThread))
                    {
                        _thread = null;
                    }
                }
            }
        }

        private bool TryReadPose(out PoseSample sample)
        {
            IPoseObservationSource observationSource = _source as IPoseObservationSource;
            if (observationSource != null)
            {
                PoseObservation observation;
                if (!observationSource.TryGetLatestObservation(out observation))
                {
                    sample = default(PoseSample);
                    return false;
                }

                sample = observation.Sample;
                _store.Publish(sample);
                if (_observationStore != null)
                {
                    _observationStore.Publish(observation);
                }

                return true;
            }

            if (!_source.TryGetLatest(out sample))
            {
                return false;
            }

            _store.Publish(sample);
            return true;
        }

        private void SetConnected()
        {
            bool recovered;
            lock (_sync)
            {
                recovered = _lastError != null;
                _connected = true;
                _lastError = null;
                _lastReportedError = null;
            }

            if (recovered)
            {
                _logger.Information("pose.worker.recovered", "Pose source connection recovered.");
            }
        }

        private void SetSampleReceived(PoseSample sample)
        {
            bool recovered;
            lock (_sync)
            {
                recovered = _lastError != null;
                _connected = true;
                _lastError = null;
                _lastSuccessfulSampleTicks = sample.TimestampTicks;
                _lastReportedError = null;
            }

            if (recovered)
            {
                _logger.Information("pose.worker.recovered", "Pose samples recovered.");
            }
        }

        private void SetFailure(bool connected, string error)
        {
            bool shouldLog;
            lock (_sync)
            {
                _connected = connected;
                _lastError = error;
                shouldLog = !string.Equals(_lastReportedError, error, StringComparison.Ordinal);
                if (shouldLog)
                {
                    _lastReportedError = error;
                }
            }

            if (shouldLog)
            {
                _logger.Warning("pose.worker.failure", error);
            }
        }

        private static bool Wait(CancellationToken cancellation, int milliseconds)
        {
            return cancellation.WaitHandle.WaitOne(milliseconds);
        }

        private static int FailureDelayMilliseconds(int failureCount)
        {
            if (failureCount <= 1)
            {
                return 100;
            }

            if (failureCount == 2)
            {
                return 500;
            }

            if (failureCount == 3)
            {
                return 2000;
            }

            return 5000;
        }
    }
}