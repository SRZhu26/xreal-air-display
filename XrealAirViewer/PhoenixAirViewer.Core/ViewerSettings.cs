using System;

namespace PhoenixAirViewer.Core
{
    public sealed class ViewerSettings
    {
        public const int CurrentSchemaVersion = 1;

        public ViewerSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            Pose = new PosePipelineSettings();
            Panel = new PanelSettings();
            RecenterHotkey = "Ctrl+Alt+Space";
            FileLoggingEnabled = true;
        }

        public int SchemaVersion { get; set; }
        public PosePipelineSettings Pose { get; set; }
        public PanelSettings Panel { get; set; }
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

            Pose.Validate();
            Panel.Validate();
            if (string.IsNullOrWhiteSpace(RecenterHotkey))
            {
                throw new InvalidOperationException("The recenter hotkey cannot be empty.");
            }
        }
    }
}