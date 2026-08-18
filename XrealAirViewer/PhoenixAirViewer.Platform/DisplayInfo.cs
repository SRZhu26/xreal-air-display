using System;
using System.Drawing;

namespace PhoenixAirViewer.Platform
{
    public sealed class DisplayInfo
    {
        internal DisplayInfo(int index, string deviceName, Rectangle bounds, Rectangle workArea, bool isPrimary)
        {
            Index = index;
            DeviceName = deviceName;
            Bounds = bounds;
            WorkArea = workArea;
            IsPrimary = isPrimary;
        }

        public int Index { get; private set; }
        public string DeviceName { get; private set; }
        public Rectangle Bounds { get; private set; }
        public Rectangle WorkArea { get; private set; }
        public bool IsPrimary { get; private set; }

        public override string ToString()
        {
            string primary = IsPrimary ? " (primary)" : string.Empty;
            return string.Format("{0}{1}: {2}x{3} at {4},{5}", DeviceName, primary, Bounds.Width, Bounds.Height, Bounds.Left, Bounds.Top);
        }
    }
}
