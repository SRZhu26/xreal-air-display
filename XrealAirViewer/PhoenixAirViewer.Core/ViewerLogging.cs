using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

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
        private readonly BlockingCollection<LogRecord> _queue;
        private readonly Thread _writerThread;
        private StreamWriter _writer;
        private bool _disposed;
        private string _lastWriteError;
        private bool _failureReported;
        private long _droppedRecordCount;

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
            _queue = new BlockingCollection<LogRecord>(new ConcurrentQueue<LogRecord>(), 1024);
            _writerThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "Phoenix Air diagnostic writer"
            };
            _writerThread.Start();
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        public bool IsEnabled
        {
            get { return true; }
        }

        public string LastWriteError
        {
            get
            {
                lock (_sync)
                {
                    return _lastWriteError;
                }
            }
        }

        public long DroppedRecordCount
        {
            get
            {
                lock (_sync)
                {
                    return _droppedRecordCount;
                }
            }
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
            LogRecord record = new LogRecord
            {
                Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Level = level.ToString(),
                Event = eventName ?? string.Empty,
                Message = message ?? string.Empty,
                Exception = exception == null ? null : exception.ToString()
            };

            bool dropped = false;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    if (!_queue.TryAdd(record))
                    {
                        _droppedRecordCount++;
                        dropped = true;
                    }
                }
                catch (InvalidOperationException)
                {
                    _droppedRecordCount++;
                    dropped = true;
                }
            }

            if (dropped)
            {
                ReportFailure("The diagnostic log queue is full; a record was dropped.");
            }
        }

        public void Dispose()
        {
            bool stopped;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _queue.CompleteAdding();
            }

            stopped = _writerThread == Thread.CurrentThread || _writerThread.Join(3000);
            if (!stopped)
            {
                ReportFailure("The diagnostic writer did not stop within the shutdown timeout.");
                return;
            }

            lock (_sync)
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                }
            }

            _queue.Dispose();
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

        private void WriteLoop()
        {
            try
            {
                foreach (LogRecord record in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        RotateIfNeeded();
                        if (_writer == null)
                        {
                            _writer = OpenWriter();
                        }

                        _writer.WriteLine(JsonSerializer.Serialize(record, SerializerOptions));
                        lock (_sync)
                        {
                            _lastWriteError = null;
                            _failureReported = false;
                        }
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(exception.ToString());
                    }
                }
            }
            catch (Exception exception)
            {
                ReportFailure(exception.ToString());
            }
        }

        private void RotateIfNeeded()
        {
            if (_writer == null)
            {
                _writer = OpenWriter();
            }

            if (_writer.BaseStream.Length < MaximumFileBytes)
            {
                return;
            }

            StreamWriter oldWriter = _writer;
            _writer = null;
            try
            {
                oldWriter.Flush();
                oldWriter.Dispose();
                string rotatedPath = _filePath + ".1";
                if (File.Exists(rotatedPath))
                {
                    File.Delete(rotatedPath);
                }

                File.Move(_filePath, rotatedPath);
                _writer = OpenWriter();
            }
            catch (Exception exception)
            {
                try
                {
                    oldWriter.Dispose();
                }
                catch (Exception disposeException)
                {
                    ReportFailure(disposeException.ToString());
                }

                ReportFailure(exception.ToString());
                _writer = OpenWriter();
            }
        }

        private void ReportFailure(string message)
        {
            bool shouldReport;
            lock (_sync)
            {
                _lastWriteError = message;
                shouldReport = !_failureReported;
                _failureReported = true;
            }

            if (shouldReport)
            {
                try
                {
                    Console.Error.WriteLine("PhoenixAirViewer logging failure: " + message);
                }
                catch
                {
                }
            }
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