using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace PhoenixAirViewer.Platform
{
    public sealed class D3D11DeviceResources : IDisposable
    {
        private bool _disposed;

        private D3D11DeviceResources(ID3D11Device device, ID3D11DeviceContext context, FeatureLevel featureLevel)
        {
            Device = device;
            Context = context;
            FeatureLevel = featureLevel;
        }

        public ID3D11Device Device { get; private set; }
        public ID3D11DeviceContext Context { get; private set; }
        public FeatureLevel FeatureLevel { get; private set; }

        public static D3D11DeviceResources CreateForAdapter(IDXGIAdapter adapter)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException("adapter");
            }

            FeatureLevel[] featureLevels =
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0
            };

            ID3D11Device device;
            ID3D11DeviceContext context;
            FeatureLevel selectedFeatureLevel;
            D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device,
                out selectedFeatureLevel,
                out context).CheckError();

            return new D3D11DeviceResources(device, context, selectedFeatureLevel);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Context != null)
            {
                Context.Dispose();
                Context = null;
            }

            if (Device != null)
            {
                Device.Dispose();
                Device = null;
            }
        }
    }
}
