using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace PhoenixAirViewer.Core
{
    public sealed class ViewerSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

        private readonly string _filePath;

        public ViewerSettingsStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A settings file path is required.", "filePath");
            }

            _filePath = filePath;
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public string LastLoadError { get; private set; }

        public static ViewerSettingsStore CreateDefault()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoenixAirViewer");
            return new ViewerSettingsStore(Path.Combine(directory, "settings.json"));
        }

        public ViewerSettings Load()
        {
            LastLoadError = null;
            string backupPath = _filePath + ".bak";
            if (!File.Exists(_filePath))
            {
                if (File.Exists(backupPath))
                {
                    try
                    {
                        LastLoadError = "The primary settings file was missing; the backup was restored.";
                        return LoadFromFile(backupPath);
                    }
                    catch (Exception backupException)
                    {
                        LastLoadError = "The primary settings file was missing and the backup could not be loaded: " + backupException.Message;
                    }
                }

                return new ViewerSettings();
            }

            try
            {
                return LoadFromFile(_filePath);
            }
            catch (Exception primaryException)
            {
                if (File.Exists(backupPath))
                {
                    try
                    {
                        ViewerSettings backupSettings = LoadFromFile(backupPath);
                        LastLoadError = "The primary settings file was invalid; the backup was restored: " + primaryException.Message;
                        return backupSettings;
                    }
                    catch (Exception backupException)
                    {
                        LastLoadError = "The primary and backup settings files were invalid: " + primaryException.Message + " / " + backupException.Message;
                        return new ViewerSettings();
                    }
                }

                LastLoadError = primaryException.Message;
                return new ViewerSettings();
            }
        }

        public void Save(ViewerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            settings.Validate();
            string directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("The settings file must include a directory.");
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = _filePath + ".bak";
            try
            {
                string json = JsonSerializer.Serialize(settings, SerializerOptions);
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(_filePath))
                {
                    File.Replace(temporaryPath, _filePath, backupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, _filePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new QuaternionJsonConverter());
            return options;
        }

        private static ViewerSettings LoadFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                ViewerSettings settings = JsonSerializer.Deserialize<ViewerSettings>(json, SerializerOptions);
                if (settings == null)
                {
                    throw new InvalidDataException("The settings file is empty.");
                }

                Migrate(settings, document.RootElement);
                settings.Validate();
                return settings;
            }
        }

        private static void Migrate(ViewerSettings settings, JsonElement root)
        {
            if (settings.SchemaVersion > ViewerSettings.CurrentSchemaVersion)
            {
                return;
            }

            MigratePanelCurvature(settings, root);

            if (settings.Pose == null)
            {
                return;
            }

            if (settings.SchemaVersion <= 1 && PoseMath.AngularDistanceRadians(settings.Pose.SensorToRenderer, Quaternion.Identity) < 0.001f)
            {
                settings.Pose.SensorToRenderer = PosePipelineSettings.DefaultAirSensorToRenderer;
            }

            if (PoseMath.AngularDistanceRadians(settings.Pose.SensorToRenderer, PosePipelineSettings.LegacyDefaultAirSensorToRenderer) < 0.001f)
            {
                settings.Pose.SensorToRenderer = PosePipelineSettings.DefaultAirSensorToRenderer;
            }

            if (settings.SchemaVersion <= 2 && Math.Abs(settings.Pose.SmoothingTimeConstantSeconds - 0.035f) < 0.0001f)
            {
                settings.Pose.SmoothingTimeConstantSeconds = 0.0f;
            }

            if (settings.SchemaVersion <= 2 && Math.Abs(settings.Pose.MaxAngularVelocityDegreesPerSecond - 720.0f) < 0.0001f)
            {
                settings.Pose.MaxAngularVelocityDegreesPerSecond = 0.0f;
            }

            if (settings.SchemaVersion <= 3)
            {
                settings.Pose.PitchSensitivity = PosePipelineSettings.DefaultPitchSensitivity;
                settings.Pose.YawSensitivity = PosePipelineSettings.DefaultYawSensitivity;
                settings.Pose.RollSensitivity = PosePipelineSettings.DefaultRollSensitivity;
            }

            if (settings.SchemaVersion <= 6 || settings.DistanceProfiles == null || settings.DistanceProfiles.Count != 4 ||
                FindProfile(settings.DistanceProfiles, "Middle") != null || FindProfile(settings.DistanceProfiles, "Medium") != null)
            {
                IList<DistanceProfileSettings> profiles = DistanceProfileSettings.CreateDefaults(settings.Pose);
                DistanceProfileSettings previousNearProfile = FindProfile(settings.DistanceProfiles, "Near");
                DistanceProfileSettings previousMidProfile = FindProfile(settings.DistanceProfiles, "Mid") ?? FindProfile(settings.DistanceProfiles, "Middle");
                DistanceProfileSettings previousFarProfile = FindProfile(settings.DistanceProfiles, "Medium") ?? FindProfile(settings.DistanceProfiles, "Far");
                DistanceProfileSettings previousFurthestProfile = FindProfile(settings.DistanceProfiles, "Furthest");
                for (int index = 0; index < profiles.Count; index++)
                {
                    profiles[index].PitchSensitivity = settings.Pose.PitchSensitivity;
                    profiles[index].YawSensitivity = settings.Pose.YawSensitivity;
                    profiles[index].RollSensitivity = settings.Pose.RollSensitivity;
                    profiles[index].TranslationSensitivity = settings.Panel.TranslationSensitivity;
                }

                CopyProfileTuning(previousNearProfile, FindProfile(profiles, "Near"));
                CopyProfileTuning(previousMidProfile, FindProfile(profiles, "Mid"));
                CopyProfileTuning(previousFarProfile, FindProfile(profiles, "Far"));
                CopyProfileTuning(previousFurthestProfile, FindProfile(profiles, "Furthest"));

                int nearestIndex = FindNearestProfile(settings.Panel.PanelDistanceMeters, profiles);
                string activeProfile = NormalizeProfileKey(settings.ActiveDistanceProfile);
                int activeIndex = FindProfileIndex(profiles, activeProfile);
                if (activeIndex >= 0)
                {
                    nearestIndex = activeIndex;
                }
                if (settings.Panel != null)
                {
                    profiles[nearestIndex].PanelDistanceMeters = settings.Panel.PanelDistanceMeters;
                }
                settings.DistanceProfiles = profiles;
                settings.ActiveDistanceProfile = profiles[nearestIndex].Key;
            }

            settings.SchemaVersion = ViewerSettings.CurrentSchemaVersion;
        }

        private static void MigratePanelCurvature(ViewerSettings settings, JsonElement root)
        {
            if (settings.Panel == null || !root.TryGetProperty("Panel", out JsonElement panel))
            {
                return;
            }

            bool hasX = panel.TryGetProperty("CurvatureRadiusXMeters", out JsonElement curvatureX);
            bool hasY = panel.TryGetProperty("CurvatureRadiusYMeters", out JsonElement curvatureY);
            if (hasX && hasY)
            {
                return;
            }

            if (panel.TryGetProperty("CurvatureRadiusMeters", out JsonElement legacyCurvature))
            {
                float radius = legacyCurvature.GetSingle();
                if (!hasX)
                {
                    settings.Panel.CurvatureRadiusXMeters = radius;
                }

                if (!hasY)
                {
                    settings.Panel.CurvatureRadiusYMeters = radius;
                }
            }
            else if (hasX && !hasY)
            {
                settings.Panel.CurvatureRadiusYMeters = curvatureX.GetSingle();
            }
            else if (!hasX && hasY)
            {
                settings.Panel.CurvatureRadiusXMeters = curvatureY.GetSingle();
            }
        }

        private static int FindNearestProfile(float distance, IList<DistanceProfileSettings> profiles)
        {
            int nearestIndex = 0;
            float nearestDifference = Math.Abs(distance - profiles[0].PanelDistanceMeters);
            for (int index = 1; index < profiles.Count; index++)
            {
                float difference = Math.Abs(distance - profiles[index].PanelDistanceMeters);
                if (difference < nearestDifference)
                {
                    nearestIndex = index;
                    nearestDifference = difference;
                }
            }

            return nearestIndex;
        }

        private static DistanceProfileSettings FindProfile(IList<DistanceProfileSettings> profiles, string key)
        {
            if (profiles == null)
            {
                return null;
            }

            for (int index = 0; index < profiles.Count; index++)
            {
                if (profiles[index] != null && string.Equals(profiles[index].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return profiles[index];
                }
            }

            return null;
        }

        private static int FindProfileIndex(IList<DistanceProfileSettings> profiles, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return -1;
            }

            for (int index = 0; index < profiles.Count; index++)
            {
                if (profiles[index] != null && string.Equals(profiles[index].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string NormalizeProfileKey(string key)
        {
            if (string.Equals(key, "Middle", StringComparison.OrdinalIgnoreCase))
            {
                return "Mid";
            }

            if (string.Equals(key, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                return "Far";
            }

            return key;
        }

        private static void CopyProfileTuning(DistanceProfileSettings source, DistanceProfileSettings destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            destination.PanelDistanceMeters = source.PanelDistanceMeters;
            destination.PitchSensitivity = source.PitchSensitivity;
            destination.YawSensitivity = source.YawSensitivity;
            destination.RollSensitivity = source.RollSensitivity;
            destination.TranslationSensitivity = source.TranslationSensitivity;
        }
    }
}