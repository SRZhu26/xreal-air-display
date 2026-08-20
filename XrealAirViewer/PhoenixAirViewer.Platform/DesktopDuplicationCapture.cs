using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        private string _lastAcquiredSourcePixels;
        private bool _disposed;

        public DesktopDuplicationCapture(DisplayInfo display)
        {
            if (display == null)
            {
                throw new ArgumentNullException("display");
            }

            _display = display;
            IDXGIFactory1 factory = null;
            IDXGIAdapter1 adapter = null;
            IDXGIOutput output = null;
            IDXGIOutput1 output1 = null;
            D3D11DeviceResources deviceResources = null;
            IDXGIOutputDuplication duplication = null;
            try
            {
                factory = CreateDXGIFactory1<IDXGIFactory1>();
                FindOutput(factory, display.DeviceName, out adapter, out output);
                deviceResources = D3D11DeviceResources.CreateForAdapter(adapter);
                output1 = output.QueryInterface<IDXGIOutput1>();
                duplication = output1.DuplicateOutput(deviceResources.Device);
                _factory = factory;
                _adapter = adapter;
                _output = output;
                _output1 = output1;
                _deviceResources = deviceResources;
                _duplication = duplication;
            }
            catch
            {
                if (duplication != null)
                {
                    duplication.Dispose();
                }

                if (output1 != null)
                {
                    output1.Dispose();
                }

                if (deviceResources != null)
                {
                    deviceResources.Dispose();
                }

                if (output != null)
                {
                    output.Dispose();
                }

                if (adapter != null)
                {
                    adapter.Dispose();
                }

                if (factory != null)
                {
                    factory.Dispose();
                }

                throw;
            }
        }

        public DisplayInfo Display
        {
            get { return _display; }
        }

        public D3D11DeviceResources DeviceResources
        {
            get { return _deviceResources; }
        }

        public long AdapterLuid
        {
            get { return (long)_adapter.Description1.Luid; }
        }

        public string LastAcquiredSourcePixels
        {
            get { return _lastAcquiredSourcePixels; }
        }

        public string DescribeLatestTexturePixels()
        {
            ThrowIfDisposed();
            return _latestTexture == null ? "latestPixels=unavailable" : DescribeTexturePixels(_latestTexture).Replace("sourcePixels=", "latestPixels=");
        }

        public bool TryReadLatestPixel(uint x, uint y, out uint packedBgra)
        {
            ThrowIfDisposed();
            packedBgra = 0;
            if (_latestTexture == null || x >= _latestWidth || y >= _latestHeight)
            {
                return false;
            }

            Texture2DDescription stagingDescription = _latestTexture.Description;
            stagingDescription.Usage = ResourceUsage.Staging;
            stagingDescription.BindFlags = BindFlags.None;
            stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
            stagingDescription.MiscFlags = ResourceOptionFlags.None;
            using (ID3D11Texture2D stagingTexture = _deviceResources.Device.CreateTexture2D(stagingDescription))
            {
                _deviceResources.Context.CopyResource(stagingTexture, _latestTexture);
                _deviceResources.Context.Flush();
                MappedSubresource mappedSubresource;
                _deviceResources.Context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out mappedSubresource).CheckError();
                try
                {
                    IntPtr pixelPointer = IntPtr.Add(mappedSubresource.DataPointer, checked((int)(y * mappedSubresource.RowPitch + x * 4)));
                    packedBgra = unchecked((uint)Marshal.ReadInt32(pixelPointer));
                    return true;
                }
                finally
                {
                    _deviceResources.Context.Unmap(stagingTexture, 0);
                }
            }
        }

        public string DescribeLatestTexture()
        {
            ThrowIfDisposed();
            if (_latestTexture == null)
            {
                return "none";
            }

            Texture2DDescription description = _latestTexture.Description;
            return string.Format(
                "format={0}; width={1}; height={2}; mipLevels={3}; arraySize={4}; usage={5}; bindFlags={6}; miscFlags={7}",
                description.Format,
                description.Width,
                description.Height,
                description.MipLevels,
                description.ArraySize,
                description.Usage,
                description.BindFlags,
                description.MiscFlags);
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
                    if (_lastAcquiredSourcePixels == null)
                    {
                        _lastAcquiredSourcePixels = DescribeTexturePixels(sourceTexture);
                    }

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

        private string DescribeTexturePixels(ID3D11Texture2D sourceTexture)
        {
            Texture2DDescription sourceDescription = sourceTexture.Description;
            Texture2DDescription stagingDescription = sourceDescription;
            stagingDescription.Usage = ResourceUsage.Staging;
            stagingDescription.BindFlags = BindFlags.None;
            stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
            stagingDescription.MiscFlags = ResourceOptionFlags.None;
            using (ID3D11Texture2D stagingTexture = _deviceResources.Device.CreateTexture2D(stagingDescription))
            {
                _deviceResources.Context.CopyResource(stagingTexture, sourceTexture);
                _deviceResources.Context.Flush();
                MappedSubresource mappedSubresource;
                _deviceResources.Context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out mappedSubresource).CheckError();
                try
                {
                    uint center = ReadPixel(mappedSubresource, sourceDescription.Width / 2, sourceDescription.Height / 2);
                    uint topLeft = ReadPixel(mappedSubresource, 0, 0);
                    uint topRight = ReadPixel(mappedSubresource, sourceDescription.Width - 1, 0);
                    uint bottomLeft = ReadPixel(mappedSubresource, 0, sourceDescription.Height - 1);
                    uint bottomRight = ReadPixel(mappedSubresource, sourceDescription.Width - 1, sourceDescription.Height - 1);
                    return string.Format("sourcePixels=0x{0:X8},0x{1:X8},0x{2:X8},0x{3:X8},0x{4:X8}", topLeft, topRight, center, bottomLeft, bottomRight);
                }
                finally
                {
                    _deviceResources.Context.Unmap(stagingTexture, 0);
                }
            }
        }

        private static uint ReadPixel(MappedSubresource mappedSubresource, uint x, uint y)
        {
            IntPtr pixelPointer = IntPtr.Add(mappedSubresource.DataPointer, checked((int)(y * mappedSubresource.RowPitch + x * 4)));
            return unchecked((uint)Marshal.ReadInt32(pixelPointer));
        }

        private void FindOutput(IDXGIFactory1 factory, string deviceName, out IDXGIAdapter1 adapter, out IDXGIOutput output)
        {
            adapter = null;
            output = null;
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                IDXGIAdapter1 candidateAdapter;
                Result adapterResult = factory.EnumAdapters1(adapterIndex, out candidateAdapter);
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
