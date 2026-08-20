using System;
using System.Drawing;

namespace PhoenixAirViewer.Platform
{
    public sealed class DisplayInfo
    {
        internal DisplayInfo(
            int index,
            string deviceName,
            Rectangle bounds,
            Rectangle workArea,
            bool isPrimary,
            string deviceString,
            string deviceId,
            string deviceInterfaceName)
        {
            Index = index;
            DeviceName = deviceName;
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
            DeviceString = deviceString;
            DeviceId = deviceId;
            DeviceInterfaceName = deviceInterfaceName;
        }

        public int Index { get; private set; }
        public string DeviceName { get; private set; }
        public Rectangle Bounds { get; private set; }
        public Rectangle WorkArea { get; private set; }
        public bool IsPrimary { get; private set; }
        public string DeviceString { get; private set; }
        public string DeviceId { get; private set; }
        public string DeviceInterfaceName { get; private set; }

        public override string ToString()
        {
            string primary = IsPrimary ? " (primary)" : string.Empty;
            string identity = string.Join(
                " ",
                new[]
                {
                    DeviceString,
                    string.IsNullOrWhiteSpace(DeviceId) ? null : "id=" + DeviceId
                });
            return string.Format("{0}{1}: {2}x{3} at {4},{5}{6}", DeviceName, primary, Bounds.Width, Bounds.Height, Bounds.Left, Bounds.Top, identity);
        }
    }
}
