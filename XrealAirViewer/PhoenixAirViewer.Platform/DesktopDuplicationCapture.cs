using System;
using System.Diagnostics;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;
using DxgiResultCode = Vortice.DXGI.ResultCode;

namespace PhoenixAirViewer.Platform
{
    public enum DesktopCaptureStatus
    {
        FrameReady,
        Timeout,
        AccessLost,
        DeviceRemoved,
        Error
    }

    public sealed class DesktopCaptureFrame
    {
        internal DesktopCaptureFrame(ID3D11ShaderResourceView textureView, uint width, uint height, long timestampTicks)
        {
            TextureView = textureView;
            Width = width;
            Height = height;
            TimestampTicks = timestampTicks;
        }

        public ID3D11ShaderResourceView TextureView { get; private set; }
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public long TimestampTicks { get; private set; }
    }

    public sealed class DesktopCaptureResult
    {
        private DesktopCaptureResult(DesktopCaptureStatus status, DesktopCaptureFrame frame, string error)
        {
            Status = status;
            Frame = frame;
            Error = error;
        }

        public DesktopCaptureStatus Status { get; private set; }
        public DesktopCaptureFrame Frame { get; private set; }
        public string Error { get; private set; }

        internal static DesktopCaptureResult Ready(DesktopCaptureFrame frame)
        {
            return new DesktopCaptureResult(DesktopCaptureStatus.FrameReady, frame, null);
        }

        internal static DesktopCaptureResult State(DesktopCaptureStatus status)
        {
            return new DesktopCaptureResult(status, null, null);
        }

        internal static DesktopCaptureResult Failed(DesktopCaptureStatus status, string error)
        {
            return new DesktopCaptureResult(status, null, error);
        }
    }

    public sealed class DesktopDuplicationCapture : IDisposable
    {
        private readonly DisplayInfo _display;
        private readonly IDXGIFactory1 _factory;
        private readonly IDXGIAdapter1 _adapter;
        private readonly IDXGIOutput _output;
        private readonly IDXGIOutput1 _output1;
        private readonly D3D11DeviceResources _deviceResources;
        private readonly IDXGIOutputDuplication _duplication;
        private ID3D11Texture2D _latestTexture;
        private ID3D11ShaderResourceView _latestTextureView;
        private uint _latestWidth;
        private uint _latestHeight;
        private bool _disposed;

        public DesktopDuplicationCapture(DisplayInfo display)
        {
            if (display == null)
            {
                throw new ArgumentNullException("display");
            }

            _display = display;
            _factory = CreateDXGIFactory1<IDXGIFactory1>();
            FindOutput(display.DeviceName, out _adapter, out _output);
            _deviceResources = D3D11DeviceResources.CreateForAdapter(_adapter);
            _output1 = _output.QueryInterface<IDXGIOutput1>();
            _duplication = _output1.DuplicateOutput(_deviceResources.Device);
        }

        public DisplayInfo Display
        {
            get { return _display; }
        }

        public D3D11DeviceResources DeviceResources
        {
            get { return _deviceResources; }
        }

        public DesktopCaptureResult TryAcquire(uint timeoutMilliseconds)
        {
            ThrowIfDisposed();

            OutduplFrameInfo frameInfo;
            IDXGIResource desktopResource;
            Result result = _duplication.AcquireNextFrame(timeoutMilliseconds, out frameInfo, out desktopResource);
            if (result == DxgiResultCode.WaitTimeout)
            {
                return DesktopCaptureResult.State(DesktopCaptureStatus.Timeout);
            }

            if (result == DxgiResultCode.AccessLost)
            {
                return DesktopCaptureResult.State(DesktopCaptureStatus.AccessLost);
            }

            if (result == DxgiResultCode.DeviceRemoved || result == DxgiResultCode.DeviceReset || result == DxgiResultCode.DeviceHung)
            {
                return DesktopCaptureResult.State(DesktopCaptureStatus.DeviceRemoved);
            }

            if (result.Failure)
            {
                return DesktopCaptureResult.Failed(DesktopCaptureStatus.Error, result.ToString());
            }

            try
            {
                using (ID3D11Texture2D sourceTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
                {
                    Texture2DDescription sourceDescription = sourceTexture.Description;
                    EnsureDestination(sourceDescription);
                    _deviceResources.Context.CopyResource(_latestTexture, sourceTexture);
                    long timestampTicks = frameInfo.LastPresentTime > 0 ? frameInfo.LastPresentTime : Stopwatch.GetTimestamp();
                    return DesktopCaptureResult.Ready(new DesktopCaptureFrame(_latestTextureView, _latestWidth, _latestHeight, timestampTicks));
                }
            }
            catch (Exception exception)
            {
                return DesktopCaptureResult.Failed(DesktopCaptureStatus.Error, exception.Message);
            }
            finally
            {
                if (desktopResource != null)
                {
                    desktopResource.Dispose();
                }

                _duplication.ReleaseFrame();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_latestTextureView != null)
            {
                _latestTextureView.Dispose();
                _latestTextureView = null;
            }

            if (_latestTexture != null)
            {
                _latestTexture.Dispose();
                _latestTexture = null;
            }

            _duplication.Dispose();
            _output1.Dispose();
            _deviceResources.Dispose();
            _output.Dispose();
            _adapter.Dispose();
            _factory.Dispose();
        }

        private void EnsureDestination(Texture2DDescription sourceDescription)
        {
            if (_latestTexture != null && _latestWidth == sourceDescription.Width && _latestHeight == sourceDescription.Height)
            {
                return;
            }

            if (_latestTextureView != null)
            {
                _latestTextureView.Dispose();
                _latestTextureView = null;
            }

            if (_latestTexture != null)
            {
                _latestTexture.Dispose();
                _latestTexture = null;
            }

            Texture2DDescription destinationDescription = sourceDescription;
            destinationDescription.Usage = ResourceUsage.Default;
            destinationDescription.BindFlags = BindFlags.ShaderResource;
            destinationDescription.CPUAccessFlags = CpuAccessFlags.None;
            destinationDescription.MiscFlags = ResourceOptionFlags.None;
            _latestTexture = _deviceResources.Device.CreateTexture2D(destinationDescription);
            _latestTextureView = _deviceResources.Device.CreateShaderResourceView(_latestTexture, null);
            _latestWidth = sourceDescription.Width;
            _latestHeight = sourceDescription.Height;
        }

        private void FindOutput(string deviceName, out IDXGIAdapter1 adapter, out IDXGIOutput output)
        {
            adapter = null;
            output = null;
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                IDXGIAdapter1 candidateAdapter;
                Result adapterResult = _factory.EnumAdapters1(adapterIndex, out candidateAdapter);
                if (adapterResult.Failure)
                {
                    break;
                }

                bool found = false;
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    IDXGIOutput candidateOutput;
                    Result outputResult = candidateAdapter.EnumOutputs(outputIndex, out candidateOutput);
                    if (outputResult.Failure)
                    {
                        break;
                    }

                    OutputDescription description = candidateOutput.Description;
                    if (string.Equals(description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        adapter = candidateAdapter;
                        output = candidateOutput;
                        found = true;
                        break;
                    }

                    candidateOutput.Dispose();
                }

                if (found)
                {
                    return;
                }

                candidateAdapter.Dispose();
            }

            throw new InvalidOperationException("The selected display is not available through DXGI: " + deviceName);
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
