using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using PhoenixAirViewer.Core;
using static Vortice.DXGI.DXGI;
using D3D11Device = Vortice.Direct3D11.D3D11;
using DxgiResultCode = Vortice.DXGI.ResultCode;

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
    float4 CursorInfo;
    float4 CursorImageInfo;
    float4 OverlayInfo;
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
Texture2D CursorTexture : register(t1);

cbuffer TransformBuffer : register(b0)
{
    row_major float4x4 WorldViewProjection;
    float4 CursorInfo;
    float4 CursorImageInfo;
    float4 OverlayInfo;
};

bool IsCursorShape(float2 pixel, float expansion)
{
    bool arrow = pixel.x >= -expansion &&
        pixel.y >= -expansion &&
        pixel.y <= 22.0f + expansion &&
        pixel.x <= pixel.y * 0.68f + 2.0f + expansion;
    bool tail = pixel.x >= 7.0f - expansion &&
        pixel.x <= 13.0f + expansion &&
        pixel.y >= 14.0f - expansion &&
        pixel.y <= 27.0f + expansion;
    return arrow || tail;
}

float4 PSMain(float4 position : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET
{
    uint textureWidth;
    uint textureHeight;
    DesktopTexture.GetDimensions(textureWidth, textureHeight);
    uint2 maximumTexel = uint2(textureWidth - 1, textureHeight - 1);
    float2 positionInTexture = saturate(texCoord) * float2(maximumTexel.x, maximumTexel.y);
    uint2 lowerTexel = uint2(positionInTexture);
    uint2 upperTexel = min(lowerTexel + uint2(1, 1), maximumTexel);
    float2 weight = positionInTexture - float2(lowerTexel.x, lowerTexel.y);
    float4 top = lerp(
        DesktopTexture.Load(int3(lowerTexel, 0)),
        DesktopTexture.Load(int3(uint2(upperTexel.x, lowerTexel.y), 0)),
        weight.x);
    float4 bottom = lerp(
        DesktopTexture.Load(int3(uint2(lowerTexel.x, upperTexel.y), 0)),
        DesktopTexture.Load(int3(upperTexel, 0)),
        weight.x);
    float4 color = lerp(top, bottom, weight.y);
    if (CursorInfo.z > 0.5f)
    {
        if (CursorInfo.w > 0.5f)
        {
            float2 cursorPixel = (texCoord - CursorInfo.xy) * float2(textureWidth, textureHeight);
            float2 imagePixel = cursorPixel + CursorImageInfo.zw;
            if (imagePixel.x >= 0.0f && imagePixel.y >= 0.0f &&
                imagePixel.x < CursorImageInfo.x && imagePixel.y < CursorImageInfo.y)
            {
                uint2 cursorTexel = uint2(imagePixel);
                float4 cursorColor = CursorTexture.Load(int3(cursorTexel, 0));
                color = lerp(color, cursorColor, cursorColor.a);
            }
        }
        else
        {
            float2 cursorPixel = (texCoord - CursorInfo.xy) * float2(textureWidth, textureHeight);
            if (IsCursorShape(cursorPixel, 1.8f))
            {
                color = float4(0.02f, 0.02f, 0.02f, 1.0f);
            }
            if (IsCursorShape(cursorPixel, 0.0f))
            {
                color = float4(1.0f, 1.0f, 1.0f, 1.0f);
            }
        }
    }
    if (OverlayInfo.x > 0.5f)
    {
        float2 edgeDistance = min(texCoord, 1.0f - texCoord);
        if (edgeDistance.x < 0.004f || edgeDistance.y < 0.004f)
        {
            color = lerp(color, float4(0.10f, 0.85f, 1.0f, 1.0f), 0.90f);
        }
    }
    return color;
}
";

        private readonly D3D11DeviceResources _deviceResources;
        private readonly IDXGIFactory1 _factory;
        private readonly IDXGISwapChain _swapChain;
        private ID3D11RenderTargetView _renderTargetView;
        private readonly ID3D11VertexShader _vertexShader;
        private readonly ID3D11PixelShader _pixelShader;
        private readonly ID3D11InputLayout _inputLayout;
        private ID3D11Buffer _positionBuffer;
        private ID3D11Buffer _textureCoordinateBuffer;
        private ID3D11Buffer _indexBuffer;
        private readonly ID3D11Buffer _transformBuffer;
        private readonly ID3D11ShaderResourceView[] _pixelShaderResources = new ID3D11ShaderResourceView[2];
        private readonly IntPtr _outputWindowHandle;
        private ID3D11Texture2D _cursorTexture;
        private ID3D11ShaderResourceView _cursorTextureView;
        private IntPtr _cursorTextureHandle;
        private int _cursorTextureWidth;
        private int _cursorTextureHeight;
        private uint _width;
        private uint _height;
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
            _outputWindowHandle = outputWindowHandle;
            _width = width;
            _height = height;
            _panelSettings = panelSettings.Clone();
            try
            {
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
                        new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 0, 1)
                    },
                    vertexShaderBytecode);

                RebuildMesh();
                _transformBuffer = _deviceResources.Device.CreateBuffer(new TransformBuffer[1], BindFlags.ConstantBuffer);
            }
            catch
            {
                Dispose();
                throw;
            }
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

        public bool Render(DesktopCaptureFrame frame)
        {
            return Render(frame, Quaternion.Identity);
        }

        public bool Render(DesktopCaptureFrame frame, Quaternion cameraOrientation)
        {
            return Render(frame, cameraOrientation, Vector3.Zero, false);
        }

        public bool Render(DesktopCaptureFrame frame, Quaternion cameraOrientation, Vector3 worldOffset, bool headFollowing)
        {
            return Render(frame, cameraOrientation, worldOffset, headFollowing, DesktopCursorState.Hidden);
        }

        public bool Render(
            DesktopCaptureFrame frame,
            Quaternion cameraOrientation,
            Vector3 worldOffset,
            bool headFollowing,
            DesktopCursorState cursor)
        {
            if (frame == null)
            {
                throw new ArgumentNullException("frame");
            }

            ThrowIfDisposed();
            if (!TryResizeToWindow())
            {
                return false;
            }

            EnsureCursorTexture(cursor);

            ID3D11DeviceContext context = _deviceResources.Context;
            context.OMSetRenderTargets(_renderTargetView);
            context.RSSetViewports(new[] { new Viewport(0.0f, 0.0f, _width, _height) });
            context.ClearRenderTargetView(_renderTargetView, new Color4(0.015f, 0.02f, 0.03f, 1.0f));
            TransformBuffer transform = new TransformBuffer
            {
                WorldViewProjection = PanelViewTransform.CreateWorldViewProjection(
                    cameraOrientation,
                    worldOffset,
                    headFollowing,
                    _width,
                    _height),
                CursorInfo = cursor == null || !cursor.Visible
                    ? Vector4.Zero
                    : new Vector4(
                        cursor.TextureX,
                        cursor.TextureY,
                        1.0f,
                        cursor.HasImage && _cursorTextureView != null ? 1.0f : 0.0f),
                CursorImageInfo = cursor == null || !cursor.HasImage
                    ? Vector4.Zero
                    : new Vector4(cursor.ImageWidth, cursor.ImageHeight, cursor.HotspotX, cursor.HotspotY),
                OverlayInfo = new Vector4(headFollowing ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f)
            };
            context.UpdateSubresource(transform, _transformBuffer);
            context.IASetInputLayout(_inputLayout);
            context.IASetVertexBuffers(
                0,
                new[] { _positionBuffer, _textureCoordinateBuffer },
                new uint[] { 12, 8 },
                new uint[] { 0, 0 });
            context.IASetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader);
            context.VSSetConstantBuffer(0, _transformBuffer);
            context.PSSetShader(_pixelShader);
            context.PSSetConstantBuffer(0, _transformBuffer);
            _pixelShaderResources[0] = frame.TextureView;
            _pixelShaderResources[1] = _cursorTextureView;
            context.PSSetShaderResources(0, 2, _pixelShaderResources);
            context.DrawIndexed(_indexCount, 0, 0);
            _pixelShaderResources[0] = null;
            _pixelShaderResources[1] = null;
            context.PSSetShaderResources(0, 2, _pixelShaderResources);
            Result presentResult = _swapChain.Present(1, PresentFlags.None);
            if (presentResult == DxgiResultCode.WasStillDrawing)
            {
                return false;
            }

            presentResult.CheckError();
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_transformBuffer != null)
            {
                _transformBuffer.Dispose();
            }

            DisposeCursorTexture();

            DisposeMesh();
            if (_inputLayout != null)
            {
                _inputLayout.Dispose();
            }

            if (_pixelShader != null)
            {
                _pixelShader.Dispose();
            }

            if (_vertexShader != null)
            {
                _vertexShader.Dispose();
            }

            if (_renderTargetView != null)
            {
                _renderTargetView.Dispose();
            }

            if (_swapChain != null)
            {
                _swapChain.Dispose();
            }

            if (_factory != null)
            {
                _factory.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private void EnsureCursorTexture(DesktopCursorState cursor)
        {
            if (cursor == null || !cursor.Visible || !cursor.HasImage)
            {
                return;
            }

            if (_cursorTextureView != null &&
                _cursorTextureHandle == cursor.CursorHandle &&
                _cursorTextureWidth == cursor.ImageWidth &&
                _cursorTextureHeight == cursor.ImageHeight)
            {
                return;
            }

            DisposeCursorTexture();
            try
            {
                Texture2DDescription description = new Texture2DDescription
                {
                    Width = (uint)cursor.ImageWidth,
                    Height = (uint)cursor.ImageHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write,
                    MiscFlags = ResourceOptionFlags.None
                };
                _cursorTexture = _deviceResources.Device.CreateTexture2D(description);
                _cursorTextureView = _deviceResources.Device.CreateShaderResourceView(_cursorTexture, null);

                MappedSubresource mappedSubresource;
                _deviceResources.Context.Map(
                    _cursorTexture,
                    0,
                    MapMode.WriteDiscard,
                    Vortice.Direct3D11.MapFlags.None,
                    out mappedSubresource).CheckError();
                try
                {
                    int rowBytes = checked(cursor.ImageWidth * 4);
                    for (int row = 0; row < cursor.ImageHeight; row++)
                    {
                        Marshal.Copy(
                            cursor.PixelData,
                            row * rowBytes,
                            IntPtr.Add(mappedSubresource.DataPointer, checked(row * (int)mappedSubresource.RowPitch)),
                            rowBytes);
                    }
                }
                finally
                {
                    _deviceResources.Context.Unmap(_cursorTexture, 0);
                }

                _cursorTextureHandle = cursor.CursorHandle;
                _cursorTextureWidth = cursor.ImageWidth;
                _cursorTextureHeight = cursor.ImageHeight;
            }
            catch
            {
                DisposeCursorTexture();
            }
        }

        private void DisposeCursorTexture()
        {
            if (_cursorTextureView != null)
            {
                _cursorTextureView.Dispose();
                _cursorTextureView = null;
            }

            if (_cursorTexture != null)
            {
                _cursorTexture.Dispose();
                _cursorTexture = null;
            }

            _cursorTextureHandle = IntPtr.Zero;
            _cursorTextureWidth = 0;
            _cursorTextureHeight = 0;
        }

        private bool TryResizeToWindow()
        {
            NativeRect clientRect;
            if (!GetClientRect(_outputWindowHandle, out clientRect))
            {
                return true;
            }

            uint width = (uint)Math.Max(0, clientRect.Right - clientRect.Left);
            uint height = (uint)Math.Max(0, clientRect.Bottom - clientRect.Top);
            if (width == 0 || height == 0)
            {
                return false;
            }

            if (width == _width && height == _height)
            {
                return true;
            }

            ID3D11DeviceContext context = _deviceResources.Context;
            context.OMSetRenderTargets((ID3D11RenderTargetView)null);
            if (_renderTargetView != null)
            {
                _renderTargetView.Dispose();
                _renderTargetView = null;
            }

            _swapChain.ResizeBuffers(0, width, height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            using (ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0))
            {
                _renderTargetView = _deviceResources.Device.CreateRenderTargetView(backBuffer);
            }

            _width = width;
            _height = height;
            return true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect clientRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private void RebuildMesh()
        {
            DisposeMesh();

            int horizontalSegments = _panelSettings.CurvatureRadiusXMeters > 0.0f ? 32 : 1;
            int verticalSegments = _panelSettings.CurvatureRadiusYMeters > 0.0f ? 16 : 1;
            PositionVertex[] positions = new PositionVertex[(horizontalSegments + 1) * (verticalSegments + 1)];
            TextureCoordinateVertex[] textureCoordinates = new TextureCoordinateVertex[positions.Length];
            for (int y = 0; y <= verticalSegments; y++)
            {
                float textureY = y / (float)verticalSegments;
                for (int x = 0; x <= horizontalSegments; x++)
                {
                    float textureX = x / (float)horizontalSegments;
                    Vector3 position = PanelGeometry.CreatePosition(_panelSettings, textureX, textureY);

                    int vertexIndex = y * (horizontalSegments + 1) + x;
                    positions[vertexIndex] = new PositionVertex(position.X, position.Y, position.Z);
                    textureCoordinates[vertexIndex] = new TextureCoordinateVertex(textureX, textureY);
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

            _positionBuffer = _deviceResources.Device.CreateBuffer(positions, BindFlags.VertexBuffer);
            _textureCoordinateBuffer = _deviceResources.Device.CreateBuffer(textureCoordinates, BindFlags.VertexBuffer);
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

            if (_textureCoordinateBuffer != null)
            {
                _textureCoordinateBuffer.Dispose();
                _textureCoordinateBuffer = null;
            }

            if (_positionBuffer != null)
            {
                _positionBuffer.Dispose();
                _positionBuffer = null;
            }
        }

        private struct PositionVertex
        {
            public PositionVertex(float positionX, float positionY, float positionZ)
            {
                PositionX = positionX;
                PositionY = positionY;
                PositionZ = positionZ;
            }

            public float PositionX;
            public float PositionY;
            public float PositionZ;
        }

        private struct TextureCoordinateVertex
        {
            public TextureCoordinateVertex(float textureX, float textureY)
            {
                TextureX = textureX;
                TextureY = textureY;
            }

            public float TextureX;
            public float TextureY;
        }

        private struct TransformBuffer
        {
            public Matrix4x4 WorldViewProjection;
            public Vector4 CursorInfo;
            public Vector4 CursorImageInfo;
            public Vector4 OverlayInfo;
        }
    }
}
