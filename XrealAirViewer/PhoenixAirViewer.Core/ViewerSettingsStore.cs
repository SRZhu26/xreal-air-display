using System;
using System.IO;
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
            if (!File.Exists(_filePath))
            {
                return new ViewerSettings();
            }

            try
            {
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                ViewerSettings settings = JsonSerializer.Deserialize<ViewerSettings>(json, SerializerOptions);
                if (settings == null)
                {
                    throw new InvalidDataException("The settings file is empty.");
                }

                settings.Validate();
                return settings;
            }
            catch (Exception exception)
            {
                LastLoadError = exception.Message;
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
    }
}