using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using PhoenixAirViewer.Core;
using PhoenixAirViewer.Platform;

namespace PhoenixAirViewer.App
{
    internal sealed class PoseEvidenceForm : Form
    {
        private readonly LatestPoseStore _poseStore;
        private readonly LatestPoseObservationStore _observationStore;
        private readonly PosePipeline _posePipeline;
        private readonly PoseEvidenceCaptureService _captureService;
        private readonly Func<IList<DiagnosticScreenshotTarget>> _targetProvider;
        private readonly Func<PosePresentationSnapshot> _presentationProvider;
        private readonly Func<DesktopViewerSession> _viewerSessionProvider;
        private readonly Func<DistanceProfileSettings> _profileProvider;
        private readonly Func<string> _connectionProvider;
        private readonly Func<string> _errorProvider;
        private readonly Label _poseLabel;
        private readonly Label _statusLabel;
        private readonly Timer _telemetryTimer;
        private readonly Dictionary<string, DateTime> _pendingCaptures = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private int _nextSequence;
        private bool _disposed;

        public PoseEvidenceForm(
            LatestPoseStore poseStore,
            LatestPoseObservationStore observationStore,
            PosePipeline posePipeline,
            PoseEvidenceCaptureService captureService,
            Func<IList<DiagnosticScreenshotTarget>> targetProvider,
            Func<PosePresentationSnapshot> presentationProvider,
            Func<DesktopViewerSession> viewerSessionProvider,
            Func<DistanceProfileSettings> profileProvider,
            Func<string> connectionProvider,
            Func<string> errorProvider)
        {
            _poseStore = poseStore ?? throw new ArgumentNullException("poseStore");
            _observationStore = observationStore;
            _posePipeline = posePipeline ?? throw new ArgumentNullException("posePipeline");
            _captureService = captureService ?? throw new ArgumentNullException("captureService");
            _targetProvider = targetProvider;
            _presentationProvider = presentationProvider;
            _viewerSessionProvider = viewerSessionProvider;
            _profileProvider = profileProvider;
            _connectionProvider = connectionProvider;
            _errorProvider = errorProvider;

            Text = "XrealAirViewer - Pose Evidence";
            ClientSize = new Size(760, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label titleLabel = new Label
            {
                AutoSize = true,
                Location = new Point(20, 18),
                Font = new Font(Font, FontStyle.Bold),
                Text = "Pose evidence capture"
            };
            Label instructionLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 48),
                Size = new Size(710, 42),
                Text = "Hold the labeled head position, press its button once, and keep holding it for the full three-second countdown. Target angles are instructions; raw and mapped measurements are recorded."
            };
            _poseLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 98),
                Size = new Size(710, 42),
                Text = "Pose: waiting for a fresh sample"
            };
            _statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 365),
                Size = new Size(710, 60),
                Text = "Evidence: no captures yet"
            };
            Controls.Add(titleLabel);
            Controls.Add(instructionLabel);
            Controls.Add(_poseLabel);
            Controls.Add(_statusLabel);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Location = new Point(20, 150),
                Size = new Size(710, 205),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true
            };
            IList<PoseEvidenceTarget> targets = PoseEvidenceTargets.CreateDefault();
            for (int index = 0; index < targets.Count; index++)
            {
                PoseEvidenceTarget target = targets[index];
                Button button = new Button
                {
                    Width = 215,
                    Height = 44,
                    Margin = new Padding(4),
                    Text = FormatTarget(target),
                    Tag = target
                };
                button.Click += CaptureButton_Click;
                buttonPanel.Controls.Add(button);
            }

            Button closeButton = new Button
            {
                Width = 105,
                Height = 32,
                Text = "Close",
                Location = new Point(625, 425)
            };
            closeButton.Click += delegate { Close(); };
            Controls.Add(buttonPanel);
            Controls.Add(closeButton);

            _captureService.Completed += CaptureService_Completed;
            _telemetryTimer = new Timer { Interval = 100 };
            _telemetryTimer.Tick += TelemetryTimer_Tick;
            _telemetryTimer.Start();
            FormClosed += PoseEvidenceForm_FormClosed;
        }

        private void CaptureButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            PoseEvidenceTarget target = button == null ? null : button.Tag as PoseEvidenceTarget;
            if (target == null || _disposed)
            {
                return;
            }

            int sequence = ++_nextSequence;
            DateTime pressedUtc = DateTime.UtcNow;
            long pressedTicks = PoseClock.NowTicks();
            PosePipelineSettings settings = _posePipeline.Settings;
            DistanceProfileSettings profile = _profileProvider == null ? null : _profileProvider();
            PoseEvidencePose pose = ReadPose(pressedTicks);
            PosePresentationSnapshot presentation = _presentationProvider == null ? null : _presentationProvider();
            IList<DiagnosticScreenshotTarget> screenshotTargets = _targetProvider == null
                ? new List<DiagnosticScreenshotTarget>()
                : _targetProvider();
            screenshotTargets = AddDesktopTarget(screenshotTargets);
            PoseEvidenceRecord record = new PoseEvidenceRecord
            {
                SchemaVersion = 1,
                EvidenceId = sequence.ToString("0000") + "-" + target.Label,
                Sequence = sequence,
                Label = target.Label,
                TargetAxis = target.TargetAxis,
                TargetAngleDegrees = target.TargetAngleDegrees,
                PitchSensitivity = settings.PitchSensitivity,
                YawSensitivity = settings.YawSensitivity,
                RollSensitivity = settings.RollSensitivity,
                TranslationSensitivity = profile == null ? 0.0f : profile.TranslationSensitivity,
                PitchDriftRateDegreesPerSecond = settings.PitchDriftRateDegreesPerSecond,
                YawDriftRateDegreesPerSecond = settings.YawDriftRateDegreesPerSecond,
                ActiveDistanceProfile = profile == null ? null : profile.Key,
                PressedUtc = pressedUtc,
                PressedMonotonicTicks = pressedTicks,
                PoseAtPress = pose,
                PoseUsedForLastPresentation = PoseEvidenceFactory.CreatePresentation(presentation),
                ConnectionState = _connectionProvider == null ? null : _connectionProvider(),
                LastError = _errorProvider == null ? null : _errorProvider(),
                CameraMode = "world-locked",
                CaptureDueUtc = pressedUtc.AddMilliseconds(PoseEvidenceCaptureService.CaptureDelayMilliseconds),
                Screenshots = new List<PoseEvidenceScreenshot>(),
                CaptureStatus = "pending"
            };
            FillDisplayMetadata(record, screenshotTargets);
            bool queued = _captureService.Enqueue(record, screenshotTargets, GetViewerSession(presentation));
            if (queued)
            {
                _pendingCaptures[record.EvidenceId] = record.CaptureDueUtc;
                UpdateCaptureStatus();
            }
            else
            {
                _statusLabel.Text = "Evidence " + record.EvidenceId + " could not be queued; the record was marked failed.";
            }
        }

        private PoseEvidencePose ReadPose(long nowTicks)
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
                : (observation.Sample.AgeSeconds(nowTicks) <= 0.5 ? "fresh" : "stale");
            return PoseEvidenceFactory.CreatePose(observation, settings.SensorToRenderer, hasNeutral, neutral, nowTicks, status);
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            UpdateCaptureStatus();
            PoseEvidencePose pose = ReadPose(PoseClock.NowTicks());
            if (pose.Status == "missing")
            {
                _poseLabel.Text = "Pose: missing; connection=" + (_connectionProvider == null ? "unknown" : _connectionProvider());
                return;
            }

            PoseEvidenceQuaternion decoded = pose.DecodedQuaternion;
            PoseEvidenceQuaternion mapped = pose.MappedQuaternion;
            _poseLabel.Text = string.Format(
                "Pose: {0}, age={1:0.0} ms; native=({2:0.000},{3:0.000},{4:0.000},{5:0.000}); decoded=({6:0.000},{7:0.000},{8:0.000},{9:0.000}); mapped=({10:0.000},{11:0.000},{12:0.000},{13:0.000})",
                pose.Status,
                pose.AgeMilliseconds,
                pose.NativeComponents == null ? 0.0f : pose.NativeComponents.X,
                pose.NativeComponents == null ? 0.0f : pose.NativeComponents.Y,
                pose.NativeComponents == null ? 0.0f : pose.NativeComponents.Z,
                pose.NativeComponents == null ? 0.0f : pose.NativeComponents.W,
                decoded.X,
                decoded.Y,
                decoded.Z,
                decoded.W,
                mapped.X,
                mapped.Y,
                mapped.Z,
                mapped.W);
        }

        private void CaptureService_Completed(PoseEvidenceRecord record)
        {
            if (_disposed || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    _pendingCaptures.Remove(record.EvidenceId);
                    _statusLabel.Text = "Evidence " + record.EvidenceId + " capture status: " + record.CaptureStatus + ". Session artifacts: manifest.json and evidence.jsonl.";
                    UpdateCaptureStatus();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void PoseEvidenceForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _disposed = true;
            _telemetryTimer.Stop();
            _telemetryTimer.Dispose();
            _captureService.Completed -= CaptureService_Completed;
        }

        private DesktopViewerSession GetViewerSession(PosePresentationSnapshot presentation)
        {
            return _viewerSessionProvider == null ? null : _viewerSessionProvider();
        }

        private void UpdateCaptureStatus()
        {
            if (_pendingCaptures.Count == 0)
            {
                return;
            }

            string latestEvidenceId = null;
            DateTime latestDueUtc = DateTime.MaxValue;
            foreach (KeyValuePair<string, DateTime> pendingCapture in _pendingCaptures)
            {
                if (pendingCapture.Value < latestDueUtc)
                {
                    latestEvidenceId = pendingCapture.Key;
                    latestDueUtc = pendingCapture.Value;
                }
            }

            double remainingSeconds = (latestDueUtc - DateTime.UtcNow).TotalSeconds;
            string remainingText = remainingSeconds <= 0.0
                ? "capturing now"
                : "capture in " + remainingSeconds.ToString("0.0") + " seconds";
            _statusLabel.Text = "Evidence " + latestEvidenceId + ": hold the pose; " + remainingText + ". Pending captures: " + _pendingCaptures.Count + ".";
        }

        private static IList<DiagnosticScreenshotTarget> AddDesktopTarget(IList<DiagnosticScreenshotTarget> targets)
        {
            List<DiagnosticScreenshotTarget> result = new List<DiagnosticScreenshotTarget>
            {
                new DiagnosticScreenshotTarget("desktop", "virtual-desktop", SystemInformation.VirtualScreen)
            };
            if (targets != null)
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    if (targets[index] != null)
                    {
                        result.Add(targets[index]);
                    }
                }
            }

            return result;
        }

        private static void FillDisplayMetadata(PoseEvidenceRecord record, IList<DiagnosticScreenshotTarget> targets)
        {
            record.Displays = new List<PoseEvidenceDisplay>();
            if (targets == null)
            {
                return;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                DiagnosticScreenshotTarget target = targets[index];
                if (target == null || target.Role == "desktop")
                {
                    continue;
                }

                record.Displays.Add(new PoseEvidenceDisplay
                {
                    Role = target.Role,
                    DeviceName = target.DisplayName,
                    Left = target.Bounds.Left,
                    Top = target.Bounds.Top,
                    Width = target.Bounds.Width,
                    Height = target.Bounds.Height
                });
                if (target.Role == "source")
                {
                    record.SourceDisplayName = target.DisplayName;
                }
                else if (target.Role == "output")
                {
                    record.OutputDisplayName = target.DisplayName;
                }
            }
        }

        private static string FormatTarget(PoseEvidenceTarget target)
        {
            if (target.TargetAxis == "none")
            {
                return "Capture Neutral (0 deg)";
            }

            string direction = target.Label.EndsWith("left", StringComparison.OrdinalIgnoreCase)
                ? "Left"
                : (target.Label.EndsWith("right", StringComparison.OrdinalIgnoreCase) ? "Right" : target.Label);
            string axis = char.ToUpperInvariant(target.TargetAxis[0]) + target.TargetAxis.Substring(1);
            return "Capture " + axis + " " + direction + " (" + target.TargetAngleDegrees.ToString("0") + " deg)";
        }
    }
}