using System;
using System.Numerics;
using System.Runtime.InteropServices;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public sealed class AirPoseSource : IPoseSource
    {
        private readonly object _sync = new object();
        private readonly AirQuaternionLayout _layout;
        private readonly IViewerLogger _logger;
        private bool _connected;
        private bool _disposed;
        private string _lastError;

        public AirPoseSource(AirQuaternionLayout layout)
            : this(layout, null)
        {
        }

        public AirPoseSource(AirQuaternionLayout layout, IViewerLogger logger)
        {
            _layout = layout;
            _logger = logger ?? NullViewerLogger.Instance;
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

        public bool TryConnect(out string error)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_connected)
                {
                    error = null;
                    return true;
                }

                try
                {
                    int result = AirApiNative.StartConnection();
                    if (result != 1)
                    {
                        error = "AirAPI_Windows.StartConnection returned " + result + ".";
                        _lastError = error;
                        return false;
                    }

                    _connected = true;
                    _lastError = null;
                    _logger.Information("air.connected", "AirAPI_Windows.StartConnection succeeded.");
                    error = null;
                    return true;
                }
                catch (DllNotFoundException exception)
                {
                    return Fail(exception.Message, out error);
                }
                catch (BadImageFormatException exception)
                {
                    return Fail(exception.Message, out error);
                }
                catch (EntryPointNotFoundException exception)
                {
                    return Fail(exception.Message, out error);
                }
                catch (Exception exception)
                {
                    return Fail(exception.Message, out error);
                }
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (!_connected)
                {
                    return;
                }

                try
                {
                    AirApiNative.StopConnection();
                }
                catch (Exception exception)
                {
                    _lastError = exception.Message;
                }
                finally
                {
                    _connected = false;
                    _logger.Information("air.disconnected", "AirAPI_Windows.StopConnection completed.");
                }
            }
        }

        public bool TryGetLatest(out PoseSample sample)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                sample = default(PoseSample);
                if (!_connected)
                {
                    return false;
                }

                try
                {
                    IntPtr pointer = AirApiNative.GetQuaternion();
                    if (pointer == IntPtr.Zero)
                    {
                        _lastError = "AirAPI_Windows.GetQuaternion returned a null pointer.";
                        _logger.Warning("air.pose.invalid", _lastError);
                        return false;
                    }

                    float[] values = new float[4];
                    Marshal.Copy(pointer, values, 0, values.Length);
                    Quaternion orientation = ToQuaternion(values);
                    if (!PoseMath.TryNormalize(orientation, out orientation))
                    {
                        _lastError = "AirAPI_Windows.GetQuaternion returned an invalid quaternion.";
                        _logger.Warning("air.pose.invalid", _lastError);
                        return false;
                    }

                    sample = new PoseSample(PoseClock.NowTicks(), orientation);
                    _lastError = null;
                    return true;
                }
                catch (EntryPointNotFoundException exception)
                {
                    _lastError = "The native AirAPI does not export GetQuaternion: " + exception.Message;
                    return false;
                }
                catch (AccessViolationException exception)
                {
                    _lastError = "The native AirAPI returned unreadable pose memory: " + exception.Message;
                    return false;
                }
                catch (Exception exception)
                {
                    _lastError = exception.Message;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                Disconnect();
                _disposed = true;
            }
        }

        private Quaternion ToQuaternion(float[] values)
        {
            if (_layout == AirQuaternionLayout.Wxyz)
            {
                return new Quaternion(values[1], values[2], values[3], values[0]);
            }

            return new Quaternion(values[0], values[1], values[2], values[3]);
        }

        private bool Fail(string message, out string error)
        {
            _lastError = message;
            _logger.Warning("air.connect.failed", message);
            error = message;
            return false;
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
