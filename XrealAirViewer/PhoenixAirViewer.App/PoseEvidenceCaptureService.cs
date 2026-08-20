using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using PhoenixAirViewer.Core;
using PhoenixAirViewer.Platform;

namespace PhoenixAirViewer.App
{
    internal sealed class PoseEvidenceCaptureService : IDisposable
    {
        internal const int CaptureDelayMilliseconds = 3000;
        private readonly BlockingCollection<CaptureRequest> _queue;
        private readonly PoseEvidenceSessionWriter _writer;
        private readonly LatestPoseStore _poseStore;
        private readonly LatestPoseObservationStore _observationStore;
        private readonly PosePipeline _posePipeline;
        private readonly IViewerLogger _logger;
        private readonly Thread _worker;
        private bool _disposed;

        public PoseEvidenceCaptureService(
            PoseEvidenceSessionWriter writer,
            LatestPoseStore poseStore,
            LatestPoseObservationStore observationStore,
            PosePipeline posePipeline,
            IViewerLogger logger)
        {
            _writer = writer ?? throw new ArgumentNullException("writer");
            _poseStore = poseStore ?? throw new ArgumentNullException("poseStore");
            _observationStore = observationStore;
            _posePipeline = posePipeline ?? throw new ArgumentNullException("posePipeline");
            _logger = logger ?? NullViewerLogger.Instance;
            _queue = new BlockingCollection<CaptureRequest>(8);
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "XrealAirViewer evidence capture"
            };
            _worker.Start();
        }

        public event Action<PoseEvidenceRecord> Completed;

        public bool Enqueue(
            PoseEvidenceRecord record,
            IList<DiagnosticScreenshotTarget> targets,
            DesktopViewerSession viewerSession)
        {
            if (record == null)
            {
                throw new ArgumentNullException("record");
            }

            CaptureRequest request = new CaptureRequest(record, CopyTargets(targets), viewerSession);
            lock (this)
            {
                if (_disposed || !_queue.TryAdd(request))
                {
                    record.CaptureStatus = "failed";
                    record.CapturedUtc = DateTime.UtcNow;
                    record.CaptureMonotonicTicks = PoseClock.NowTicks();
                    TryWrite(record);
                    NotifyCompleted(record);
                    return false;
                }
            }

            _logger.Information(
                "evidence.press",
                "evidenceId=" + record.EvidenceId + "; label=" + record.Label + "; poseStatus=" + (record.PoseAtPress == null ? "missing" : record.PoseAtPress.Status) + "; captureDelayMs=" + CaptureDelayMilliseconds + ".");
            _logger.Information("evidence.capture.queued", "evidenceId=" + record.EvidenceId + "; dueUtc=" + record.CaptureDueUtc.ToString("O", CultureInfo.InvariantCulture) + ".");
            return true;
        }

        public void Dispose()
        {
            Dispose(5000);
        }

        public void Dispose(int timeoutMilliseconds)
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _queue.CompleteAdding();
            }

            bool stopped = _worker.Join(timeoutMilliseconds);
            if (!stopped)
            {
                _logger.Warning("evidence.capture.stop.timeout", "The evidence capture worker did not stop before shutdown.");
            }

            CaptureRequest pending;
            while (_queue.TryTake(out pending))
            {
                pending.Record.CaptureStatus = "closedEarly";
                pending.Record.CapturedUtc = DateTime.UtcNow;
                pending.Record.CaptureMonotonicTicks = PoseClock.NowTicks();
                TryWrite(pending.Record);
                NotifyCompleted(pending.Record);
            }

            if (stopped)
            {
                _queue.Dispose();
                _writer.Dispose();
            }
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (CaptureRequest request in _queue.GetConsumingEnumerable())
                {
                    Process(request);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("evidence.capture.worker.failed", "The evidence capture worker stopped unexpectedly.", exception);
            }
        }

        private void Process(CaptureRequest request)
        {
            PoseEvidenceRecord record = request.Record;
            _logger.Information("evidence.capture.started", "evidenceId=" + record.EvidenceId + ".");
            while (DateTime.UtcNow < record.CaptureDueUtc)
            {
                int waitMilliseconds = Math.Min(100, Math.Max(1, (int)(record.CaptureDueUtc - DateTime.UtcNow).TotalMilliseconds));
                Thread.Sleep(waitMilliseconds);
            }

            try
            {
                PoseEvidencePose screenshotPose = ReadCurrentPose();
                record.PoseAtScreenshot = screenshotPose;
                if (request.ViewerSession != null)
                {
                    PosePresentationSnapshot presentation;
                    if (request.ViewerSession.TryGetLatestPresentation(out presentation))
                    {
                        record.PoseUsedForLastPresentation = PoseEvidenceFactory.CreatePresentation(presentation);
                    }
                }

                List<PoseEvidenceScreenshot> screenshots = new List<PoseEvidenceScreenshot>();
                for (int index = 0; index < request.Targets.Count; index++)
                {
                    DiagnosticScreenshotTarget target = request.Targets[index];
                    screenshots.Add(CaptureScreenshot(record, target));
                }

                record.Screenshots = screenshots;
                record.CapturedUtc = DateTime.UtcNow;
                record.CaptureMonotonicTicks = PoseClock.NowTicks();
                int completed = 0;
                for (int index = 0; index < screenshots.Count; index++)
                {
                    if (screenshots[index].Status == "complete")
                    {
                        completed++;
                    }
                }

                record.CaptureStatus = completed == screenshots.Count && completed > 0
                    ? "complete"
                    : (completed == 0 ? "failed" : "partial");
                TryWrite(record);
                _logger.Information("evidence.capture.completed", "evidenceId=" + record.EvidenceId + "; status=" + record.CaptureStatus + ".");
            }
            catch (Exception exception)
            {
                record.CapturedUtc = DateTime.UtcNow;
                record.CaptureMonotonicTicks = PoseClock.NowTicks();
                record.CaptureStatus = "failed";
                TryWrite(record);
                _logger.Error("evidence.capture.failed", "evidenceId=" + record.EvidenceId + ".", exception);
            }

            NotifyCompleted(record);
        }

        private PoseEvidencePose ReadCurrentPose()
        {
            PoseObservation observation = null;
            if (_observationStore != null)
            {
                _observationStore.TryRead(out observation);
            }

            if (observation == null)
            {
                PoseSample sample;
                if (_poseStore.TryRead(out sample))
                {
                    observation = new PoseObservation(sample, Vector4.Zero, false);
                }
            }

            PosePipelineSettings settings = _posePipeline.Settings;
            Quaternion neutral;
            bool hasNeutral = _posePipeline.TryGetNeutral(out neutral);
            string status = observation == null
                ? "missing"
                : (observation.TimestampTicks == 0 || observation.Sample.AgeSeconds(PoseClock.NowTicks()) > 0.5 ? "stale" : "fresh");
            return PoseEvidenceFactory.CreatePose(observation, settings.SensorToRenderer, hasNeutral, neutral, PoseClock.NowTicks(), status);
        }

        private PoseEvidenceScreenshot CaptureScreenshot(PoseEvidenceRecord record, DiagnosticScreenshotTarget target)
        {
            PoseEvidenceScreenshot screenshot = new PoseEvidenceScreenshot
            {
                Role = target == null ? "unknown" : target.Role,
                Status = "failed"
            };
            if (target == null || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
            {
                screenshot.Status = "skipped";
                screenshot.Error = "The target bounds were empty.";
                return screenshot;
            }

            string fileName = record.EvidenceId + "-" + SanitizeFilePart(target.Role) + ".png";
            string filePath = Path.Combine(_writer.SessionDirectory, fileName);
            screenshot.RelativePath = fileName;
            try
            {
                using (Bitmap bitmap = new Bitmap(target.Bounds.Width, target.Bounds.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(target.Bounds.Left, target.Bounds.Top, 0, 0, target.Bounds.Size, CopyPixelOperation.SourceCopy);
                    bitmap.Save(filePath, ImageFormat.Png);
                }

                screenshot.Status = "complete";
            }
            catch (Exception exception)
            {
                screenshot.Error = exception.Message;
                _logger.Error("evidence.screenshot.failed", "evidenceId=" + record.EvidenceId + "; role=" + target.Role + ".", exception);
            }

            return screenshot;
        }

        private void TryWrite(PoseEvidenceRecord record)
        {
            try
            {
                _writer.Write(record);
            }
            catch (Exception exception)
            {
                _logger.Error("evidence.write.failed", "evidenceId=" + record.EvidenceId + ".", exception);
            }
        }

        private void NotifyCompleted(PoseEvidenceRecord record)
        {
            Action<PoseEvidenceRecord> handler = Completed;
            if (handler != null)
            {
                handler(record);
            }
        }

        private static IList<DiagnosticScreenshotTarget> CopyTargets(IList<DiagnosticScreenshotTarget> targets)
        {
            List<DiagnosticScreenshotTarget> copy = new List<DiagnosticScreenshotTarget>();
            if (targets != null)
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    if (targets[index] != null)
                    {
                        copy.Add(targets[index]);
                    }
                }
            }

            return copy;
        }

        private static string SanitizeFilePart(string value)
        {
            string result = string.IsNullOrWhiteSpace(value) ? "unknown" : value;
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            for (int index = 0; index < invalidCharacters.Length; index++)
            {
                result = result.Replace(invalidCharacters[index], '_');
            }

            return result.Replace(' ', '_');
        }

        private sealed class CaptureRequest
        {
            public CaptureRequest(PoseEvidenceRecord record, IList<DiagnosticScreenshotTarget> targets, DesktopViewerSession viewerSession)
            {
                Record = record;
                Targets = targets;
                ViewerSession = viewerSession;
            }

            public PoseEvidenceRecord Record { get; private set; }
            public IList<DiagnosticScreenshotTarget> Targets { get; private set; }
            public DesktopViewerSession ViewerSession { get; private set; }
        }
    }
}