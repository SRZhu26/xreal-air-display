using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PhoenixAirViewer.Core
{
    public enum ViewerLogLevel
    {
        Debug,
        Information,
        Warning,
        Error
    }

    public interface IViewerLogger : IDisposable
    {
        bool IsEnabled { get; }
        void Write(ViewerLogLevel level, string eventName, string message, Exception exception = null);
    }

    public static class ViewerLoggerExtensions
    {
        public static void Debug(this IViewerLogger logger, string eventName, string message)
        {
            logger.Write(ViewerLogLevel.Debug, eventName, message);
        }

        public static void Information(this IViewerLogger logger, string eventName, string message)
        {
            logger.Write(ViewerLogLevel.Information, eventName, message);
        }

        public static void Warning(this IViewerLogger logger, string eventName, string message)
        {
            logger.Write(ViewerLogLevel.Warning, eventName, message);
        }

        public static void Error(this IViewerLogger logger, string eventName, string message, Exception exception = null)
        {
            logger.Write(ViewerLogLevel.Error, eventName, message, exception);
        }
    }

    public sealed class NullViewerLogger : IViewerLogger
    {
        private NullViewerLogger()
        {
        }

        public static NullViewerLogger Instance { get; } = new NullViewerLogger();

        public bool IsEnabled
        {
            get { return false; }
        }

        public void Write(ViewerLogLevel level, string eventName, string message, Exception exception = null)
        {
        }

        public void Dispose()
        {
        }
    }

    public sealed class FileViewerLogger : IViewerLogger
    {
        private const long MaximumFileBytes = 10 * 1024 * 1024;
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions();
        private readonly object _sync = new object();
        private readonly string _filePath;
        private StreamWriter _writer;
        private bool _disposed;

        public FileViewerLogger(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A log file path is required.", "filePath");
            }

            _filePath = filePath;
            string directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("The log file must include a directory.");
            }

            Directory.CreateDirectory(directory);
            _writer = OpenWriter();
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public bool IsEnabled
        {
            get { return true; }
        }

        public static FileViewerLogger CreateDefault()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoenixAirViewer",
                "logs");
            string fileName = "PhoenixAirViewer-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl";
            return new FileViewerLogger(Path.Combine(directory, fileName));
        }

        public void Write(ViewerLogLevel level, string eventName, string message, Exception exception = null)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    RotateIfNeeded();
                    LogRecord record = new LogRecord
                    {
                        Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                        Level = level.ToString(),
                        Event = eventName ?? string.Empty,
                        Message = message ?? string.Empty,
                        Exception = exception == null ? null : exception.ToString()
                    };
                    _writer.WriteLine(JsonSerializer.Serialize(record, SerializerOptions));
                    _writer.Flush();
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }
            }
        }

        private StreamWriter OpenWriter()
        {
            return new StreamWriter(
                new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        private void RotateIfNeeded()
        {
            if (_writer.BaseStream.Length < MaximumFileBytes)
            {
                return;
            }

            _writer.Dispose();
            string rotatedPath = _filePath + ".1";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }

            File.Move(_filePath, rotatedPath);
            _writer = OpenWriter();
        }

        private sealed class LogRecord
        {
            public string Utc { get; set; }
            public string Level { get; set; }
            public string Event { get; set; }
            public string Message { get; set; }
            public string Exception { get; set; }
        }
    }
}