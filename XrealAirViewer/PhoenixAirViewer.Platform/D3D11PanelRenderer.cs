using System;
using System.Numerics;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using PhoenixAirViewer.Core;
using static Vortice.DXGI.DXGI;
using D3D11Device = Vortice.Direct3D11.D3D11;

namespace PhoenixAirViewer.Platform
{
    public sealed class D3D11PanelRenderer : IDisposable
    {
        private const string VertexShaderSource = @"
struct VertexInput
{
    float3 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

cbuffer TransformBuffer : register(b0)
{
    row_major float4x4 WorldViewProjection;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
    output.TexCoord = input.TexCoord;
    return output;
}
";

        private const string PixelShaderSource = @"
Texture2D DesktopTexture : register(t0);
SamplerState LinearSampler : register(s0);

float4 PSMain(float4 position : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET
{
    return DesktopTexture.Sample(LinearSampler, texCoord);
}
";

        private readonly D3D11DeviceResources _deviceResources;
        private readonly IDXGIFactory1 _factory;
        private readonly IDXGISwapChain _swapChain;
        private readonly ID3D11RenderTargetView _renderTargetView;
        private readonly ID3D11VertexShader _vertexShader;
        private readonly ID3D11PixelShader _pixelShader;
        private readonly ID3D11InputLayout _inputLayout;
        private ID3D11Buffer _vertexBuffer;
        private ID3D11Buffer _indexBuffer;
        private readonly ID3D11Buffer _transformBuffer;
        private readonly ID3D11SamplerState _sampler;
        private readonly uint _width;
        private readonly uint _height;
        private PanelSettings _panelSettings;
        private uint _indexCount;
        private bool _disposed;

        public D3D11PanelRenderer(DesktopDuplicationCapture capture, IntPtr outputWindowHandle, uint width, uint height)
            : this(capture, outputWindowHandle, width, height, new PanelSettings())
        {
        }

        public D3D11PanelRenderer(DesktopDuplicationCapture capture, IntPtr outputWindowHandle, uint width, uint height, PanelSettings panelSettings)
        {
            if (capture == null)
            {
                throw new ArgumentNullException("capture");
            }

            if (panelSettings == null)
            {
                throw new ArgumentNullException("panelSettings");
            }

            panelSettings.Validate();

            if (outputWindowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("The output window must have a native handle.", "outputWindowHandle");
            }

            if (width == 0 || height == 0)
            {
                throw new ArgumentOutOfRangeException("width");
            }

            _deviceResources = capture.DeviceResources;
            _width = width;
            _height = height;
            _panelSettings = panelSettings.Clone();
            _factory = CreateDXGIFactory1<IDXGIFactory1>();

            SwapChainDescription swapChainDescription = new SwapChainDescription
            {
                BufferDescription = new ModeDescription(width, height, new Rational(60, 1), Format.B8G8R8A8_UNorm),
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                OutputWindow = outputWindowHandle,
                Windowed = true,
                SwapEffect = SwapEffect.FlipDiscard,
                Flags = SwapChainFlags.None
            };

            _swapChain = _factory.CreateSwapChain(_deviceResources.Device, swapChainDescription);
            using (ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0))
            {
                _renderTargetView = _deviceResources.Device.CreateRenderTargetView(backBuffer);
            }

            byte[] vertexShaderBytecode = Compiler.Compile(VertexShaderSource, "VSMain", "PhoenixAirViewer.Panel.hlsl", "vs_5_0").ToArray();
            byte[] pixelShaderBytecode = Compiler.Compile(PixelShaderSource, "PSMain", "PhoenixAirViewer.Panel.hlsl", "ps_5_0").ToArray();
            _vertexShader = _deviceResources.Device.CreateVertexShader(vertexShaderBytecode);
            _pixelShader = _deviceResources.Device.CreatePixelShader(pixelShaderBytecode);
            _inputLayout = _deviceResources.Device.CreateInputLayout(
                new[]
                {
                    new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0),
                    new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 12)
                },
                vertexShaderBytecode);

            RebuildMesh();
            _transformBuffer = _deviceResources.Device.CreateBuffer(new TransformBuffer[1], BindFlags.ConstantBuffer);
            _sampler = _deviceResources.Device.CreateSamplerState(
                new SamplerDescription(
                    Filter.MinMagMipLinear,
                    TextureAddressMode.Clamp,
                    0.0f,
                    1,
                    ComparisonFunction.Never));
        }

        public void UpdatePanelSettings(PanelSettings panelSettings)
        {
            if (panelSettings == null)
            {
                throw new ArgumentNullException("panelSettings");
            }

            panelSettings.Validate();
            ThrowIfDisposed();
            _panelSettings = panelSettings.Clone();
            RebuildMesh();
        }

        public void Render(DesktopCaptureFrame frame)
        {
            Render(frame, Quaternion.Identity);
        }

        public void Render(DesktopCaptureFrame frame, Quaternion cameraOrientation)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            ThrowIfDisposed();
            ID3D11DeviceContext context = _deviceResources.Context;
            context.OMSetRenderTargets(_renderTargetView);
            context.RSSetViewports(new[] { new Viewport(0.0f, 0.0f, _width, _height) });
            context.ClearRenderTargetView(_renderTargetView, new Color4(0.015f, 0.02f, 0.03f, 1.0f));
            Matrix4x4 world = Matrix4x4.Identity;
            Matrix4x4 view = Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(Quaternion.Normalize(cameraOrientation)));
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
                (float)(Math.PI / 3.0),
                _width / (float)_height,
                0.05f,
                100.0f);
            TransformBuffer transform = new TransformBuffer { WorldViewProjection = world * view * projection };
            context.UpdateSubresource(transform, _transformBuffer);
            context.IASetInputLayout(_inputLayout);
            context.IASetVertexBuffers(0, new[] { _vertexBuffer }, new uint[] { 20 }, new uint[] { 0 });
            context.IASetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader);
            context.VSSetConstantBuffer(0, _transformBuffer);
            context.PSSetShader(_pixelShader);
            context.PSSetShaderResource(0, frame.TextureView);
            context.PSSetSampler(0, _sampler);
            context.DrawIndexed(_indexCount, 0, 0);
            context.PSSetShaderResource(0, null);
            _swapChain.Present(0, PresentFlags.None).CheckError();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sampler.Dispose();
            _transformBuffer.Dispose();
            DisposeMesh();
            _inputLayout.Dispose();
            _pixelShader.Dispose();
            _vertexShader.Dispose();
            _renderTargetView.Dispose();
            _swapChain.Dispose();
            _factory.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private void RebuildMesh()
        {
            DisposeMesh();

            int horizontalSegments = _panelSettings.CurvatureRadiusMeters > 0.0f ? 32 : 1;
            const int verticalSegments = 1;
            Vertex[] vertices = new Vertex[(horizontalSegments + 1) * (verticalSegments + 1)];
            for (int y = 0; y <= verticalSegments; y++)
            {
                float textureY = y / (float)verticalSegments;
                float positionY = (_panelSettings.PanelHeightMeters * 0.5f) - textureY * _panelSettings.PanelHeightMeters;
                for (int x = 0; x <= horizontalSegments; x++)
                {
                    float textureX = x / (float)horizontalSegments;
                    float positionX;
                    float positionZ;
                    if (_panelSettings.CurvatureRadiusMeters > 0.0f)
                    {
                        float angle = (textureX - 0.5f) * _panelSettings.PanelWidthMeters / _panelSettings.CurvatureRadiusMeters;
                        positionX = _panelSettings.CurvatureRadiusMeters * (float)Math.Sin(angle);
                        positionZ = -_panelSettings.PanelDistanceMeters + _panelSettings.CurvatureRadiusMeters * ((float)Math.Cos(angle) - 1.0f);
                    }
                    else
                    {
                        positionX = (textureX - 0.5f) * _panelSettings.PanelWidthMeters;
                        positionZ = -_panelSettings.PanelDistanceMeters;
                    }

                    vertices[y * (horizontalSegments + 1) + x] = new Vertex(positionX, positionY, positionZ, textureX, textureY);
                }
            }

            ushort[] indices = new ushort[horizontalSegments * verticalSegments * 6];
            int index = 0;
            for (int y = 0; y < verticalSegments; y++)
            {
                for (int x = 0; x < horizontalSegments; x++)
                {
                    ushort topLeft = (ushort)(y * (horizontalSegments + 1) + x);
                    ushort topRight = (ushort)(topLeft + 1);
                    ushort bottomLeft = (ushort)((y + 1) * (horizontalSegments + 1) + x);
                    ushort bottomRight = (ushort)(bottomLeft + 1);
                    indices[index++] = topLeft;
                    indices[index++] = topRight;
                    indices[index++] = bottomLeft;
                    indices[index++] = topRight;
                    indices[index++] = bottomRight;
                    indices[index++] = bottomLeft;
                }
            }

            _vertexBuffer = _deviceResources.Device.CreateBuffer(vertices, BindFlags.VertexBuffer);
            _indexBuffer = _deviceResources.Device.CreateBuffer(indices, BindFlags.IndexBuffer);
            _indexCount = (uint)indices.Length;
        }

        private void DisposeMesh()
        {
            if (_indexBuffer != null)
            {
                _indexBuffer.Dispose();
                _indexBuffer = null;
            }

            if (_vertexBuffer != null)
            {
                _vertexBuffer.Dispose();
                _vertexBuffer = null;
            }
        }

        private struct Vertex
        {
            public Vertex(float positionX, float positionY, float positionZ, float textureX, float textureY)
            {
                Position = new Vector3(positionX, positionY, positionZ);
                TexCoord = new Vector2(textureX, textureY);
            }

            public Vector3 Position;
            public Vector2 TexCoord;
        }

        private struct TransformBuffer
        {
            public Matrix4x4 WorldViewProjection;
        }
    }
}
