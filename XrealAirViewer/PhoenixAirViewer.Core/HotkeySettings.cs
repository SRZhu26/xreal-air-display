using System;
using System.Globalization;

namespace PhoenixAirViewer.Core
{
    public static class HotkeySettings
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWindows = 0x0008;

        public static bool TryParse(string value, out uint modifiers, out uint virtualKey, out string error)
        {
            modifiers = 0;
            virtualKey = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "The recenter hotkey cannot be empty.";
                return false;
            }

            string[] parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            bool hasKey = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                uint modifier;
                if (TryParseModifier(part, out modifier))
                {
                    if ((modifiers & modifier) != 0)
                    {
                        error = "The recenter hotkey contains a duplicate modifier: " + part + ".";
                        return false;
                    }

                    modifiers |= modifier;
                    continue;
                }

                if (hasKey)
                {
                    error = "The recenter hotkey must contain exactly one key.";
                    return false;
                }

                if (!TryParseKey(part, out virtualKey))
                {
                    error = "Unsupported recenter hotkey key: " + part + ".";
                    return false;
                }

                hasKey = true;
            }

            if (modifiers == 0)
            {
                error = "The recenter hotkey must include a modifier.";
                return false;
            }

            if (!hasKey)
            {
                error = "The recenter hotkey must include a key.";
                return false;
            }

            return true;
        }

        private static bool TryParseModifier(string value, out uint modifier)
        {
            switch (value.ToUpperInvariant())
            {
                case "ALT":
                    modifier = ModAlt;
                    return true;
                case "CTRL":
                case "CONTROL":
                    modifier = ModControl;
                    return true;
                case "SHIFT":
                    modifier = ModShift;
                    return true;
                case "WIN":
                case "WINDOWS":
                    modifier = ModWindows;
                    return true;
                default:
                    modifier = 0;
                    return false;
            }
        }

        private static bool TryParseKey(string value, out uint virtualKey)
        {
            string normalized = value.ToUpperInvariant();
            if (normalized == "SPACE")
            {
                virtualKey = 0x20;
                return true;
            }

            if (normalized == "ESC" || normalized == "ESCAPE")
            {
                virtualKey = 0x1B;
                return true;
            }

            if (normalized == "ENTER")
            {
                virtualKey = 0x0D;
                return true;
            }

            if (normalized == "TAB")
            {
                virtualKey = 0x09;
                return true;
            }

            if (normalized.Length == 1 && normalized[0] >= 'A' && normalized[0] <= 'Z')
            {
                virtualKey = normalized[0];
                return true;
            }

            if (normalized.Length == 1 && normalized[0] >= '0' && normalized[0] <= '9')
            {
                virtualKey = normalized[0];
                return true;
            }

            if (normalized.Length >= 2 && normalized[0] == 'F')
            {
                int functionNumber;
                if (int.TryParse(normalized.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out functionNumber) && functionNumber >= 1 && functionNumber <= 24)
                {
                    virtualKey = (uint)(0x70 + functionNumber - 1);
                    return true;
                }
            }

            virtualKey = 0;
            return false;
        }
    }
}