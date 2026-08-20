using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.Platform
{
    public sealed class AirPoseSource : IPoseSource, IPoseObservationSource
    {
        private readonly object _sync = new object();
        private readonly AirQuaternionLayout _layout;
        private readonly IViewerLogger _logger;
        private readonly float[] _values = new float[4];
        private bool _connected;
        private bool _disposed;
        private string _lastError;
        private long _lastPoseErrorLogTicks;
        private long _lastPoseSampleLogTicks;

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

        public AirQuaternionLayout QuaternionLayout
        {
            get { return _layout; }
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

                _logger.Information("air.connect.request", DescribeNativeLoadContext());
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
                    return Fail(exception.Message, out error, exception);
                }
                catch (BadImageFormatException exception)
                {
                    return Fail(exception.Message, out error, exception);
                }
                catch (EntryPointNotFoundException exception)
                {
                    return Fail(exception.Message, out error, exception);
                }
                catch (Exception exception)
                {
                    return Fail(exception.Message, out error, exception);
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
            PoseObservation observation;
            if (!TryGetLatestObservation(out observation))
            {
                sample = default(PoseSample);
                return false;
            }

            sample = observation.Sample;
            return true;
        }

        public bool TryGetLatestObservation(out PoseObservation observation)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                observation = null;
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
                        ReportPoseError(_lastError);
                        return false;
                    }

                    Marshal.Copy(pointer, _values, 0, _values.Length);
                    Quaternion orientation = ToQuaternion(_values);
                    if (!PoseMath.TryNormalize(orientation, out orientation))
                    {
                        _lastError = "AirAPI_Windows.GetQuaternion returned an invalid quaternion.";
                        ReportPoseError(_lastError);
                        return false;
                    }

                    PoseSample sample = new PoseSample(PoseClock.NowTicks(), orientation);
                    observation = new PoseObservation(
                        sample,
                        new Vector4(_values[0], _values[1], _values[2], _values[3]),
                        true);
                    long nowTicks = PoseClock.NowTicks();
                    if (_logger.IsEnabled && nowTicks - _lastPoseSampleLogTicks >= Stopwatch.Frequency)
                    {
                        _lastPoseSampleLogTicks = nowTicks;
                        _logger.Debug(
                            "air.pose.sample",
                            "native=" + DescribeVector4(observation.NativeComponents) +
                            "; decoded=" + DescribeQuaternion(observation.Orientation) +
                            "; sampleTs=" + observation.TimestampTicks + ".");
                    }
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

        private bool Fail(string message, out string error, Exception exception = null)
        {
            _lastError = message;
            _logger.Warning("air.connect.failed", message);
            if (exception != null)
            {
                _logger.Error("air.connect.exception", DescribeNativeLoadContext(), exception);
            }

            error = message;
            return false;
        }

        private static string DescribeNativeLoadContext()
        {
            string baseDirectory = AppContext.BaseDirectory;
            return "baseDirectory=" + baseDirectory +
                "; currentDirectory=" + Environment.CurrentDirectory +
                "; processPath=" + (Environment.ProcessPath ?? string.Empty) +
                "; processArchitecture=" + RuntimeInformation.ProcessArchitecture +
                "; osArchitecture=" + RuntimeInformation.OSArchitecture +
                "; is64BitProcess=" + Environment.Is64BitProcess +
                "; AirAPI_Windows.dll=" + File.Exists(Path.Combine(baseDirectory, "AirAPI_Windows.dll")) +
                "; hidapi.dll=" + File.Exists(Path.Combine(baseDirectory, "hidapi.dll")) + ".";
        }

        private void ReportPoseError(string message)
        {
            long nowTicks = Stopwatch.GetTimestamp();
            if (nowTicks - _lastPoseErrorLogTicks >= Stopwatch.Frequency)
            {
                _lastPoseErrorLogTicks = nowTicks;
                _logger.Warning("air.pose.invalid", message);
            }
        }

        private static string DescribeVector4(Vector4 value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.000000},{1:0.000000},{2:0.000000},{3:0.000000})",
                value.X,
                value.Y,
                value.Z,
                value.W);
        }

        private static string DescribeQuaternion(Quaternion value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.000000},{1:0.000000},{2:0.000000},{3:0.000000})",
                value.X,
                value.Y,
                value.Z,
                value.W);
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
