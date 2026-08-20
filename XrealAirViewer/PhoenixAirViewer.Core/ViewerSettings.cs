using System;
using System.Collections.Generic;

namespace PhoenixAirViewer.Core
{
    public sealed class ViewerSettings
    {
        public const int CurrentSchemaVersion = 8;

        public ViewerSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            Pose = new PosePipelineSettings();
            Panel = PanelSettings.CreateWideCurvedMonitor();
            DistanceProfiles = DistanceProfileSettings.CreateDefaults(Pose);
            ActiveDistanceProfile = "Far";
            RecenterHotkey = "Ctrl+Alt+Space";
            FileLoggingEnabled = true;
        }

        public int SchemaVersion { get; set; }
        public PosePipelineSettings Pose { get; set; }
        public PanelSettings Panel { get; set; }
        public IList<DistanceProfileSettings> DistanceProfiles { get; set; }
        public string ActiveDistanceProfile { get; set; }
        public string SourceDisplayName { get; set; }
        public string OutputDisplayName { get; set; }
        public string RecenterHotkey { get; set; }
        public bool FileLoggingEnabled { get; set; }

        public ViewerSettings Clone()
        {
            return new ViewerSettings
            {
                SchemaVersion = SchemaVersion,
                Pose = Pose == null ? null : Pose.Clone(),
                Panel = Panel == null ? null : Panel.Clone(),
                DistanceProfiles = CloneProfiles(DistanceProfiles),
                ActiveDistanceProfile = ActiveDistanceProfile,
                SourceDisplayName = SourceDisplayName,
                OutputDisplayName = OutputDisplayName,
                RecenterHotkey = RecenterHotkey,
                FileLoggingEnabled = FileLoggingEnabled
            };
        }

        public void Validate()
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException("Unsupported PhoenixAirViewer settings schema: " + SchemaVersion + ".");
            }

            if (Pose == null)
            {
                throw new InvalidOperationException("Viewer pose settings are missing.");
            }

            if (Panel == null)
            {
                throw new InvalidOperationException("Viewer panel settings are missing.");
            }

            ValidateProfiles();

            Pose.Validate();
            Panel.Validate();
            uint modifiers;
            uint virtualKey;
            string hotkeyError;
            if (!HotkeySettings.TryParse(RecenterHotkey, out modifiers, out virtualKey, out hotkeyError))
            {
                throw new InvalidOperationException(hotkeyError);
            }
        }

        private void ValidateProfiles()
        {
            if (DistanceProfiles == null || DistanceProfiles.Count != 4)
            {
                throw new InvalidOperationException("Exactly four distance profiles are required.");
            }

            string[] requiredKeys = { "Near", "Mid", "Far", "Furthest" };
            for (int index = 0; index < requiredKeys.Length; index++)
            {
                DistanceProfileSettings profile = null;
                for (int profileIndex = 0; profileIndex < DistanceProfiles.Count; profileIndex++)
                {
                    if (DistanceProfiles[profileIndex] != null &&
                        string.Equals(DistanceProfiles[profileIndex].Key, requiredKeys[index], StringComparison.OrdinalIgnoreCase))
                    {
                        if (profile != null)
                        {
                            throw new InvalidOperationException("Distance profile keys must be unique.");
                        }

                        profile = DistanceProfiles[profileIndex];
                    }
                }

                if (profile == null)
                {
                    throw new InvalidOperationException("Missing distance profile: " + requiredKeys[index] + ".");
                }

                profile.Validate();
            }

            if (string.IsNullOrWhiteSpace(ActiveDistanceProfile) ||
                !ContainsProfile(ActiveDistanceProfile))
            {
                throw new InvalidOperationException("The active distance profile is invalid.");
            }
        }

        private bool ContainsProfile(string key)
        {
            for (int index = 0; index < DistanceProfiles.Count; index++)
            {
                if (DistanceProfiles[index] != null &&
                    string.Equals(DistanceProfiles[index].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IList<DistanceProfileSettings> CloneProfiles(IList<DistanceProfileSettings> profiles)
        {
            if (profiles == null)
            {
                return null;
            }

            List<DistanceProfileSettings> clone = new List<DistanceProfileSettings>();
            for (int index = 0; index < profiles.Count; index++)
            {
                clone.Add(profiles[index] == null ? null : profiles[index].Clone());
            }

            return clone;
        }
    }
}