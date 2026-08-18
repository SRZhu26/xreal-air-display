using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PhoenixAirViewer.Platform
{
    public static class DisplayEnumerator
    {
        private const uint MonitorInfoPrimary = 1;
        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect monitorRect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        public static IList<DisplayInfo> Enumerate()
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            MonitorEnumProc callback = delegate(IntPtr monitor, IntPtr hdc, ref NativeRect monitorRect, IntPtr data)
            {
                MonitorInfoEx info = new MonitorInfoEx { Size = Marshal.SizeOf(typeof(MonitorInfoEx)) };
                if (GetMonitorInfo(monitor, ref info))
                {
                    displays.Add(new DisplayInfo(
                        displays.Count,
                        info.DeviceName,
                        ToRectangle(info.Monitor),
                        ToRectangle(info.Work),
                        (info.Flags & MonitorInfoPrimary) != 0));
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                throw new InvalidOperationException("Windows could not enumerate display monitors.");
            }

            return displays;
        }

        private static Rectangle ToRectangle(NativeRect value)
        {
            return Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom);
        }
    }
}
