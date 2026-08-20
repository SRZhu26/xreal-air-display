using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PhoenixAirViewer.Platform
{
    public sealed class DesktopCursorState
    {
        private DesktopCursorState(
            bool visible,
            float textureX,
            float textureY,
            int screenX,
            int screenY,
            IntPtr cursorHandle,
            CursorImage image)
        {
            Visible = visible;
            TextureX = textureX;
            TextureY = textureY;
            ScreenX = screenX;
            ScreenY = screenY;
            CursorHandle = cursorHandle;
            if (image != null)
            {
                PixelData = image.PixelData;
                ImageWidth = image.Width;
                ImageHeight = image.Height;
                HotspotX = image.HotspotX;
                HotspotY = image.HotspotY;
            }
        }

        private static readonly object CursorImageSync = new object();
        private static bool _hasCachedCursorHandle;
        private static IntPtr _cachedCursorHandle;
        private static CursorImage _cachedCursorImage;

        public bool Visible { get; private set; }
        public float TextureX { get; private set; }
        public float TextureY { get; private set; }
        public int ScreenX { get; private set; }
        public int ScreenY { get; private set; }
        public IntPtr CursorHandle { get; private set; }
        public byte[] PixelData { get; private set; }
        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        public int HotspotX { get; private set; }
        public int HotspotY { get; private set; }

        public bool HasImage
        {
            get { return PixelData != null && ImageWidth > 0 && ImageHeight > 0; }
        }

        public static DesktopCursorState Hidden
        {
            get { return new DesktopCursorState(false, 0.0f, 0.0f, 0, 0, IntPtr.Zero, null); }
        }

        public static DesktopCursorState Read(DisplayInfo display, uint textureWidth, uint textureHeight)
        {
            if (display == null)
            {
                throw new ArgumentNullException("display");
            }

            if (textureWidth == 0 || textureHeight == 0)
            {
                return Hidden;
            }

            CursorInfo cursorInfo = new CursorInfo
            {
                Size = Marshal.SizeOf<CursorInfo>()
            };
            if (!GetCursorInfo(ref cursorInfo) || (cursorInfo.Flags & CursorShowing) == 0)
            {
                return Hidden;
            }

            Rectangle bounds = display.Bounds;
            int localX = cursorInfo.Position.X - bounds.Left;
            int localY = cursorInfo.Position.Y - bounds.Top;
            if (localX < 0 || localY < 0 || localX >= bounds.Width || localY >= bounds.Height)
            {
                return Hidden;
            }

            float textureX = textureWidth <= 1 ? 0.0f : localX / (float)(textureWidth - 1);
            float textureY = textureHeight <= 1 ? 0.0f : localY / (float)(textureHeight - 1);
            return new DesktopCursorState(
                true,
                textureX,
                textureY,
                cursorInfo.Position.X,
                cursorInfo.Position.Y,
                cursorInfo.CursorHandle,
                GetCursorImage(cursorInfo.CursorHandle));
        }

        private const int CursorShowing = 0x00000001;

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr iconHandle, out IconInfo iconInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DrawIconEx(
            IntPtr deviceContext,
            int x,
            int y,
            IntPtr iconHandle,
            int width,
            int height,
            uint step,
            IntPtr brush,
            uint flags);

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
        private static extern int GetObject(IntPtr graphicsObject, int objectSize, out BitmapObject bitmapObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr graphicsObject);

        private static CursorImage GetCursorImage(IntPtr cursorHandle)
        {
            lock (CursorImageSync)
            {
                if (_hasCachedCursorHandle && _cachedCursorHandle == cursorHandle)
                {
                    return _cachedCursorImage;
                }

                _hasCachedCursorHandle = true;
                _cachedCursorHandle = cursorHandle;
                _cachedCursorImage = ReadCursorImage(cursorHandle);
                return _cachedCursorImage;
            }
        }

        private static CursorImage ReadCursorImage(IntPtr cursorHandle)
        {
            IconInfo iconInfo;
            if (cursorHandle == IntPtr.Zero || !GetIconInfo(cursorHandle, out iconInfo))
            {
                return null;
            }

            try
            {
                IntPtr sizeBitmap = iconInfo.ColorBitmap != IntPtr.Zero ? iconInfo.ColorBitmap : iconInfo.MaskBitmap;
                BitmapObject bitmapObject;
                if (sizeBitmap == IntPtr.Zero || GetObject(sizeBitmap, Marshal.SizeOf<BitmapObject>(), out bitmapObject) == 0)
                {
                    return null;
                }

                int width = bitmapObject.Width;
                int height = bitmapObject.Height;
                if (iconInfo.ColorBitmap == IntPtr.Zero)
                {
                    height /= 2;
                }

                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    IntPtr deviceContext = graphics.GetHdc();
                    try
                    {
                        if (!DrawIconEx(deviceContext, 0, 0, cursorHandle, width, height, 0, IntPtr.Zero, 0x0003))
                        {
                            return null;
                        }
                    }
                    finally
                    {
                        graphics.ReleaseHdc(deviceContext);
                    }

                    Rectangle rectangle = new Rectangle(0, 0, width, height);
                    BitmapData bitmapData = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        byte[] pixelData = new byte[checked(width * height * 4)];
                        int rowBytes = checked(width * 4);
                        int stride = bitmapData.Stride;
                        int absoluteStride = Math.Abs(stride);
                        for (int row = 0; row < height; row++)
                        {
                            int sourceRow = stride >= 0 ? row : height - 1 - row;
                            Marshal.Copy(
                                IntPtr.Add(bitmapData.Scan0, sourceRow * absoluteStride),
                                pixelData,
                                row * rowBytes,
                                rowBytes);
                        }

                        return new CursorImage(pixelData, width, height, iconInfo.XHotspot, iconInfo.YHotspot);
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }
                }
            }
            finally
            {
                if (iconInfo.ColorBitmap != IntPtr.Zero)
                {
                    DeleteObject(iconInfo.ColorBitmap);
                }

                if (iconInfo.MaskBitmap != IntPtr.Zero)
                {
                    DeleteObject(iconInfo.MaskBitmap);
                }
            }
        }

        private sealed class CursorImage
        {
            public CursorImage(byte[] pixelData, int width, int height, int hotspotX, int hotspotY)
            {
                PixelData = pixelData;
                Width = width;
                Height = height;
                HotspotX = hotspotX;
                HotspotY = hotspotY;
            }

            public byte[] PixelData { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            public int HotspotX { get; private set; }
            public int HotspotY { get; private set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int Size;
            public int Flags;
            public IntPtr CursorHandle;
            public Point Position;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool IsIcon;
            public int XHotspot;
            public int YHotspot;
            public IntPtr MaskBitmap;
            public IntPtr ColorBitmap;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapObject
        {
            public int Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPerPixel;
            public IntPtr Bits;
        }
    }
}