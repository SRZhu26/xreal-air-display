using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.App
{
    internal sealed class DiagnosticScreenshotTarget
    {
        public DiagnosticScreenshotTarget(string role, string displayName, Rectangle bounds)
        {
            Role = role;
            DisplayName = displayName;
            Bounds = bounds;
        }

        public string Role { get; private set; }
        public string DisplayName { get; private set; }
        public Rectangle Bounds { get; private set; }
    }

    internal sealed class InteractionDiagnosticRecorder : IMessageFilter, IDisposable
    {
        private const int ScreenshotDelayMilliseconds = 3000;
        private const int MouseMoveLogIntervalMilliseconds = 250;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSetFocus = 0x0007;
        private const int WmKillFocus = 0x0008;
        private const int WmMouseMove = 0x0200;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmRightButtonDown = 0x0204;
        private const int WmRightButtonUp = 0x0205;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmMiddleButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHWheel = 0x020E;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;

        private readonly Form _rootForm;
        private readonly IViewerLogger _logger;
        private readonly Func<IList<DiagnosticScreenshotTarget>> _targetProvider;
        private readonly List<PendingCapture> _pendingCaptures = new List<PendingCapture>();
        private readonly System.Windows.Forms.Timer _captureTimer;
        private readonly string _sessionDirectory;
        private DateTime _lastMouseMoveLogUtc;
        private int _nextCaptureSequence;
        private bool _disposed;

        public InteractionDiagnosticRecorder(
            Form rootForm,
            IViewerLogger logger,
            Func<IList<DiagnosticScreenshotTarget>> targetProvider)
        {
            if (rootForm == null)
            {
                throw new ArgumentNullException("rootForm");
            }

            _rootForm = rootForm;
            _logger = logger ?? NullViewerLogger.Instance;
            _targetProvider = targetProvider;
            _sessionDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoenixAirViewer",
                "diagnostics",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(_sessionDirectory);
            _captureTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _captureTimer.Tick += CaptureTimer_Tick;
            Application.AddMessageFilter(this);
            _logger.Information("diagnostic.started", "Interaction diagnostics enabled; sessionDirectory=" + _sessionDirectory + "; screenshotDelayMs=" + ScreenshotDelayMilliseconds + ".");
        }

        public string SessionDirectory
        {
            get { return _sessionDirectory; }
        }

        public void RecordFeatureClick(string feature)
        {
            if (_disposed)
            {
                return;
            }

            string safeFeature = string.IsNullOrWhiteSpace(feature) ? "feature" : feature;
            DateTime dueUtc = DateTime.UtcNow.AddMilliseconds(ScreenshotDelayMilliseconds);
            _pendingCaptures.Add(new PendingCapture
            {
                Feature = safeFeature,
                DueUtc = dueUtc,
                Sequence = ++_nextCaptureSequence
            });
            _logger.Information(
                "ui.feature.click",
                "feature=" + safeFeature + "; screenshotDueUtc=" + dueUtc.ToString("O", CultureInfo.InvariantCulture) + ".");
            _captureTimer.Start();
        }

        public void RecordAction(string action)
        {
            if (!_disposed)
            {
                _logger.Information("ui.action", action ?? string.Empty);
            }
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (_disposed)
            {
                return false;
            }

            Control target = Control.FromHandle(message.HWnd);
            if (!BelongsToApplication(target))
            {
                return false;
            }

            switch (message.Msg)
            {
                case WmKeyDown:
                case 0x0104:
                    _logger.Debug("ui.key.down", DescribeKeyMessage(message, target));
                    break;
                case WmKeyUp:
                case 0x0105:
                    _logger.Debug("ui.key.up", DescribeKeyMessage(message, target));
                    break;
                case WmLeftButtonDown:
                case WmRightButtonDown:
                case WmMiddleButtonDown:
                case WmXButtonDown:
                    _logger.Debug("ui.mouse.down", DescribeMouseMessage(message, target));
                    break;
                case WmLeftButtonUp:
                case WmRightButtonUp:
                case WmMiddleButtonUp:
                case WmXButtonUp:
                    _logger.Debug("ui.mouse.up", DescribeMouseMessage(message, target));
                    break;
                case WmMouseWheel:
                case WmMouseHWheel:
                    _logger.Debug("ui.mouse.wheel", DescribeMouseMessage(message, target));
                    break;
                case WmMouseMove:
                    RecordMouseMove(message, target);
                    break;
                case WmSetFocus:
                    _logger.Debug("ui.focus.gained", "target=" + DescribeTarget(target) + ".");
                    break;
                case WmKillFocus:
                    _logger.Debug("ui.focus.lost", "target=" + DescribeTarget(target) + ".");
                    break;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Application.RemoveMessageFilter(this);
            _captureTimer.Stop();
            _captureTimer.Dispose();
            if (_pendingCaptures.Count > 0)
            {
                _logger.Warning("diagnostic.screenshot.cancelled", "pendingCount=" + _pendingCaptures.Count + "; reason=application-closed.");
            }
            _pendingCaptures.Clear();
            _logger.Information("diagnostic.stopped", "Interaction diagnostics stopped.");
        }

        private void CaptureTimer_Tick(object sender, EventArgs e)
        {
            DateTime nowUtc = DateTime.UtcNow;
            for (int index = _pendingCaptures.Count - 1; index >= 0; index--)
            {
                PendingCapture pending = _pendingCaptures[index];
                if (nowUtc < pending.DueUtc)
                {
                    continue;
                }

                _pendingCaptures.RemoveAt(index);
                CaptureScreenshots(pending);
            }

            if (_pendingCaptures.Count == 0)
            {
                _captureTimer.Stop();
            }
        }

        private void CaptureScreenshots(PendingCapture pending)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            string filePrefix = timestamp + "-" + pending.Sequence.ToString("0000", CultureInfo.InvariantCulture) + "-" + SanitizeFilePart(pending.Feature);
            SaveScreenshot("desktop", SystemInformation.VirtualScreen, filePrefix);

            if (_targetProvider == null)
            {
                return;
            }

            try
            {
                IList<DiagnosticScreenshotTarget> targets = _targetProvider();
                if (targets == null)
                {
                    return;
                }

                for (int index = 0; index < targets.Count; index++)
                {
                    DiagnosticScreenshotTarget target = targets[index];
                    if (target != null)
                    {
                        SaveScreenshot(target.Role, target.Bounds, filePrefix + "-" + SanitizeFilePart(target.DisplayName));
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.Error("diagnostic.screenshot.targets.failed", "The diagnostic display targets could not be resolved.", exception);
            }
        }

        private void SaveScreenshot(string role, Rectangle bounds, string filePrefix)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                _logger.Warning("diagnostic.screenshot.skipped", "role=" + role + "; bounds=" + bounds + ".");
                return;
            }

            string filePath = Path.Combine(_sessionDirectory, filePrefix + "-" + SanitizeFilePart(role) + ".png");
            try
            {
                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    bitmap.Save(filePath, ImageFormat.Png);
                }

                _logger.Information("diagnostic.screenshot.saved", "role=" + role + "; path=" + filePath + "; bounds=" + bounds + ".");
            }
            catch (Exception exception)
            {
                _logger.Error("diagnostic.screenshot.failed", "role=" + role + "; path=" + filePath + "; bounds=" + bounds + ".", exception);
            }
        }

        private void RecordMouseMove(Message message, Control target)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastMouseMoveLogUtc).TotalMilliseconds < MouseMoveLogIntervalMilliseconds)
            {
                return;
            }

            _lastMouseMoveLogUtc = nowUtc;
            _logger.Debug("ui.mouse.move", DescribeMouseMessage(message, target));
        }

        private bool BelongsToApplication(Control target)
        {
            if (target == null)
            {
                return false;
            }

            Form form = target.FindForm();
            if (form == null)
            {
                return false;
            }

            if (ReferenceEquals(form, _rootForm))
            {
                return true;
            }

            for (int index = 0; index < Application.OpenForms.Count; index++)
            {
                if (ReferenceEquals(form, Application.OpenForms[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeTarget(Control target)
        {
            if (target == null)
            {
                return "unknown";
            }

            string name = target.Name;
            ButtonBase button = target as ButtonBase;
            if (button != null && !string.IsNullOrEmpty(button.Text))
            {
                name = string.IsNullOrEmpty(name) ? button.Text : name + "(" + button.Text + ")";
            }

            return target.GetType().Name + (string.IsNullOrEmpty(name) ? string.Empty : "#" + name);
        }

        private static string DescribeKeyMessage(Message message, Control target)
        {
            string targetDescription = DescribeTarget(target);
            if (IsTextEntryControl(target))
            {
                return "message=0x" + message.Msg.ToString("X", CultureInfo.InvariantCulture) + "; target=" + targetDescription + "; key=redacted.";
            }

            return "message=0x" + message.Msg.ToString("X", CultureInfo.InvariantCulture) + "; key=" + ((Keys)message.WParam.ToInt32()).ToString() + "; target=" + targetDescription + ".";
        }

        private static bool IsTextEntryControl(Control target)
        {
            TextBoxBase textBox = target as TextBoxBase;
            if (textBox != null)
            {
                return true;
            }

            ComboBox comboBox = target as ComboBox;
            return comboBox != null && comboBox.DropDownStyle != ComboBoxStyle.DropDownList;
        }

        private static string DescribeMouseMessage(Message message, Control target)
        {
            long position = message.LParam.ToInt64();
            int x = unchecked((short)(position & 0xffff));
            int y = unchecked((short)((position >> 16) & 0xffff));
            return "message=0x" + message.Msg.ToString("X", CultureInfo.InvariantCulture) + "; x=" + x + "; y=" + y + "; target=" + DescribeTarget(target) + ".";
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

        private sealed class PendingCapture
        {
            public string Feature;
            public DateTime DueUtc;
            public int Sequence;
        }
    }
}