using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace PhoenixAirViewer.Platform
{
    public static class DisplayEnumerator
    {
        private const uint MonitorInfoPrimary = 1;
        private const uint GetDeviceInterfaceName = 1;
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumDisplayDevices(string deviceName, uint deviceIndex, ref DisplayDevice device, uint flags);

        public static IList<DisplayInfo> Enumerate()
        {
            List<DisplayInfo> displays = new List<DisplayInfo>();
            MonitorEnumProc callback = delegate(IntPtr monitor, IntPtr hdc, ref NativeRect monitorRect, IntPtr data)
            {
                MonitorInfoEx info = new MonitorInfoEx { Size = Marshal.SizeOf(typeof(MonitorInfoEx)) };
                if (GetMonitorInfo(monitor, ref info))
                {
                    DisplayDevice monitorDevice = GetMonitorDevice(info.DeviceName, 0);
                    DisplayDevice interfaceDevice = GetMonitorDevice(info.DeviceName, GetDeviceInterfaceName);
                    displays.Add(new DisplayInfo(
                        displays.Count,
                        info.DeviceName,
                        ToRectangle(info.Monitor),
                        ToRectangle(info.Work),
                        (info.Flags & MonitorInfoPrimary) != 0,
                        monitorDevice.DeviceString,
                        monitorDevice.DeviceId,
                        interfaceDevice.DeviceId));
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                throw new InvalidOperationException("Windows could not enumerate display monitors.");
            }

            return displays;
        }

        public static bool TryFindXrealDisplay(IList<DisplayInfo> displays, out DisplayInfo display, out string reason)
        {
            display = null;
            reason = null;
            if (displays == null || displays.Count == 0)
            {
                reason = "Windows did not enumerate any displays.";
                return false;
            }

            List<DisplayInfo> namedMatches = new List<DisplayInfo>();
            for (int index = 0; index < displays.Count; index++)
            {
                if (LooksLikeXreal(displays[index]))
                {
                    namedMatches.Add(displays[index]);
                }
            }

            if (namedMatches.Count == 1)
            {
                display = namedMatches[0];
                reason = "matched monitor identity: " + DescribeIdentity(display);
                return true;
            }

            if (namedMatches.Count > 1)
            {
                reason = "multiple displays matched XREAL/Nreal monitor identity; select the output explicitly.";
                return false;
            }

            List<DisplayInfo> nonPrimaryDisplays = new List<DisplayInfo>();
            for (int index = 0; index < displays.Count; index++)
            {
                if (!displays[index].IsPrimary)
                {
                    nonPrimaryDisplays.Add(displays[index]);
                }
            }

            if (nonPrimaryDisplays.Count == 1)
            {
                display = nonPrimaryDisplays[0];
                reason = "no XREAL monitor identity was exposed; selected the only non-primary display: " + DescribeIdentity(display);
                return true;
            }

            reason = nonPrimaryDisplays.Count == 0
                ? "the Air sensor is connected but Windows exposes no separate non-primary output display."
                : "the Air sensor is connected but several non-primary displays are available and none identifies as XREAL/Nreal.";
            return false;
        }

        public static bool TryGetAdapterLuid(string deviceName, out long adapterLuid)
        {
            adapterLuid = 0;
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return false;
            }

            IDXGIFactory1 factory = null;
            try
            {
                factory = CreateDXGIFactory1<IDXGIFactory1>();
                for (uint adapterIndex = 0; ; adapterIndex++)
                {
                    IDXGIAdapter1 adapter;
                    if (factory.EnumAdapters1(adapterIndex, out adapter).Failure)
                    {
                        break;
                    }

                    bool found = false;
                    try
                    {
                        for (uint outputIndex = 0; ; outputIndex++)
                        {
                            IDXGIOutput output;
                            if (adapter.EnumOutputs(outputIndex, out output).Failure)
                            {
                                break;
                            }

                            try
                            {
                                if (string.Equals(output.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                                {
                                    adapterLuid = (long)adapter.Description1.Luid;
                                    found = true;
                                    break;
                                }
                            }
                            finally
                            {
                                output.Dispose();
                            }
                        }
                    }
                    finally
                    {
                        adapter.Dispose();
                    }

                    if (found)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                adapterLuid = 0;
                return false;
            }
            finally
            {
                if (factory != null)
                {
                    factory.Dispose();
                }
            }
        }

        private static Rectangle ToRectangle(NativeRect value)
        {
            return Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom);
        }

        private static DisplayDevice GetMonitorDevice(string deviceName, uint flags)
        {
            DisplayDevice device = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
            if (!EnumDisplayDevices(deviceName, 0, ref device, flags))
            {
                return new DisplayDevice();
            }

            return device;
        }

        private static bool LooksLikeXreal(DisplayInfo display)
        {
            string identity = DescribeIdentity(display).ToUpperInvariant();
            return identity.Contains("XREAL") || identity.Contains("NREAL") || identity.Contains("NR-") || identity.Contains("MRG") || identity.Contains("AIR 1") || identity.Contains("AIR 2");
        }

        private static string DescribeIdentity(DisplayInfo display)
        {
            return string.Join(
                " ",
                new[] { display.DeviceString, display.DeviceId, display.DeviceInterfaceName });
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }
    }
}
