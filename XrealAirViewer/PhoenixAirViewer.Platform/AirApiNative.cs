using System;
using System.Runtime.InteropServices;

namespace PhoenixAirViewer.Platform
{
    internal static class AirApiNative
    {
        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int StartConnection();

        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern int StopConnection();

        [DllImport("AirAPI_Windows", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr GetQuaternion();
    }
}
