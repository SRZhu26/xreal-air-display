using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PhoenixAirViewer.Core;
using PhoenixAirViewer.Platform;

namespace PhoenixAirViewer.App
{
    public sealed class MainForm : Form
    {
    private const int WmHotKey = 0x0312;
    private const int RecenterHotKeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        private readonly AirPoseSource _poseSource;
        private readonly PosePipeline _posePipeline;
        private readonly Timer _telemetryTimer;
        private readonly Label _connectionLabel;
        private readonly Label _poseLabel;
        private readonly Label _rendererLabel;
        private readonly Button _connectButton;
        private readonly Button _disconnectButton;
        private readonly Button _recenterButton;
        private readonly ComboBox _outputDisplayCombo;
        private readonly ComboBox _sourceDisplayCombo;
        private readonly Button _outputTestButton;
        private readonly Button _captureProbeButton;
        private readonly Label _displayLabel;
        private readonly Label _captureLabel;
        private readonly ViewerSettingsStore _settingsStore;
        private readonly ViewerSettings _viewerSettings;
        private readonly IViewerLogger _logger;
        private readonly NumericUpDown _panelWidthInput;
        private readonly NumericUpDown _panelHeightInput;
        private readonly NumericUpDown _panelDistanceInput;
        private readonly NumericUpDown _curvatureRadiusInput;
        private readonly CheckBox _rollLockCheckBox;
        private readonly CheckBox _horizonLockCheckBox;
        private IList<DisplayInfo> _displays;
        private FullscreenOutputWindow _outputWindow;
        private DesktopDuplicationCapture _capture;
        private DesktopViewerSession _viewerSession;
        private bool _recenterHotKeyRegistered;

        public MainForm()
        {
            _settingsStore = ViewerSettingsStore.CreateDefault();
            _viewerSettings = _settingsStore.Load();
            _logger = CreateLogger(_viewerSettings);
            _logger.Information(
                "application.start",
                "PhoenixAirViewer starting; architecture=x64; runtime=" + Environment.Version + "; os=" + RuntimeInformation.OSDescription + "; logger=" + (_logger.IsEnabled ? "file" : "none"));
            _poseSource = new AirPoseSource(AirQuaternionLayout.Wxyz, _logger);
            _posePipeline = new PosePipeline(_viewerSettings.Pose);
            _telemetryTimer = new Timer { Interval = 33 };
            _connectionLabel = new Label();
            _poseLabel = new Label();
            _rendererLabel = new Label();
            _connectButton = new Button();
            _disconnectButton = new Button();
            _recenterButton = new Button();
            _outputDisplayCombo = new ComboBox();
            _sourceDisplayCombo = new ComboBox();
            _outputTestButton = new Button();
            _captureProbeButton = new Button();
            _displayLabel = new Label();
            _captureLabel = new Label();
            _panelWidthInput = new NumericUpDown();
            _panelHeightInput = new NumericUpDown();
            _panelDistanceInput = new NumericUpDown();
            _curvatureRadiusInput = new NumericUpDown();
            _rollLockCheckBox = new CheckBox();
            _horizonLockCheckBox = new CheckBox();

            Text = "Phoenix Air Viewer - Tracking Foundation";
            ClientSize = new Size(760, 430);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            _connectionLabel.AutoSize = true;
            _connectionLabel.Location = new Point(20, 20);
            _connectionLabel.Text = "Air: disconnected";

            _poseLabel.AutoSize = true;
            _poseLabel.Location = new Point(20, 55);
            _poseLabel.Text = "Pose: no sample";

            _rendererLabel.AutoSize = true;
            _rendererLabel.Location = new Point(20, 90);
            _rendererLabel.Text = "Renderer: D3D11 live desktop panel; recenter: Ctrl+Alt+Space";

            _connectButton.Location = new Point(20, 135);
            _connectButton.Size = new Size(120, 32);
            _connectButton.Text = "Connect Air";
            _connectButton.Click += ConnectButton_Click;

            _disconnectButton.Location = new Point(150, 135);
            _disconnectButton.Size = new Size(120, 32);
            _disconnectButton.Text = "Disconnect";
            _disconnectButton.Enabled = false;
            _disconnectButton.Click += DisconnectButton_Click;

            _recenterButton.Location = new Point(280, 135);
            _recenterButton.Size = new Size(120, 32);
            _recenterButton.Text = "Recenter";
            _recenterButton.Enabled = false;
            _recenterButton.Click += RecenterButton_Click;

            _displayLabel.AutoSize = true;
            _displayLabel.Location = new Point(20, 195);
            _displayLabel.Text = "Output monitor:";

            _outputDisplayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _outputDisplayCombo.Location = new Point(125, 190);
            _outputDisplayCombo.Size = new Size(360, 24);

            _outputTestButton.Location = new Point(495, 188);
            _outputTestButton.Size = new Size(145, 28);
            _outputTestButton.Text = "Start live desktop";
            _outputTestButton.Click += OutputTestButton_Click;

            _sourceDisplayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sourceDisplayCombo.Location = new Point(125, 230);
            _sourceDisplayCombo.Size = new Size(360, 24);

            _captureProbeButton.Location = new Point(495, 228);
            _captureProbeButton.Size = new Size(145, 28);
            _captureProbeButton.Text = "Probe capture";
            _captureProbeButton.Click += CaptureProbeButton_Click;

            _captureLabel.AutoSize = true;
            _captureLabel.Location = new Point(20, 390);
            _captureLabel.Text = "Capture: not probed";

            Label panelWidthLabel = new Label { AutoSize = true, Location = new Point(20, 305), Text = "Panel width (m):" };
            Label panelHeightLabel = new Label { AutoSize = true, Location = new Point(205, 305), Text = "Height (m):" };
            Label panelDistanceLabel = new Label { AutoSize = true, Location = new Point(365, 305), Text = "Distance (m):" };
            Label curvatureRadiusLabel = new Label { AutoSize = true, Location = new Point(20, 345), Text = "Curvature radius (m, 0 = flat):" };

            ConfigureNumericInput(_panelWidthInput, new Point(115, 300), _viewerSettings.Panel.PanelWidthMeters, 0.2m, 10.0m);
            ConfigureNumericInput(_panelHeightInput, new Point(275, 300), _viewerSettings.Panel.PanelHeightMeters, 0.2m, 10.0m);
            ConfigureNumericInput(_panelDistanceInput, new Point(445, 300), _viewerSettings.Panel.PanelDistanceMeters, 0.2m, 20.0m);
            ConfigureNumericInput(_curvatureRadiusInput, new Point(190, 340), _viewerSettings.Panel.CurvatureRadiusMeters, 0.0m, 20.0m);

            _rollLockCheckBox.AutoSize = true;
            _rollLockCheckBox.Location = new Point(365, 342);
            _rollLockCheckBox.Text = "Roll lock";
            _rollLockCheckBox.Checked = _viewerSettings.Pose.RollLock;
            _horizonLockCheckBox.AutoSize = true;
            _horizonLockCheckBox.Location = new Point(455, 342);
            _horizonLockCheckBox.Text = "Horizon lock";
            _horizonLockCheckBox.Checked = _viewerSettings.Pose.HorizonLock;
            _panelWidthInput.ValueChanged += PanelSetting_ValueChanged;
            _panelHeightInput.ValueChanged += PanelSetting_ValueChanged;
            _panelDistanceInput.ValueChanged += PanelSetting_ValueChanged;
            _curvatureRadiusInput.ValueChanged += PanelSetting_ValueChanged;
            _rollLockCheckBox.CheckedChanged += PanelSetting_ValueChanged;
            _horizonLockCheckBox.CheckedChanged += PanelSetting_ValueChanged;

            Controls.Add(_connectionLabel);
            Controls.Add(_poseLabel);
            Controls.Add(_rendererLabel);
            Controls.Add(_connectButton);
            Controls.Add(_disconnectButton);
            Controls.Add(_recenterButton);
            Controls.Add(_displayLabel);
            Controls.Add(_outputDisplayCombo);
            Controls.Add(_outputTestButton);
            Controls.Add(_sourceDisplayCombo);
            Controls.Add(_captureProbeButton);
            Controls.Add(panelWidthLabel);
            Controls.Add(panelHeightLabel);
            Controls.Add(panelDistanceLabel);
            Controls.Add(curvatureRadiusLabel);
            Controls.Add(_panelWidthInput);
            Controls.Add(_panelHeightInput);
            Controls.Add(_panelDistanceInput);
            Controls.Add(_curvatureRadiusInput);
            Controls.Add(_rollLockCheckBox);
            Controls.Add(_horizonLockCheckBox);
            Controls.Add(_captureLabel);

            LoadDisplays();

            _telemetryTimer.Tick += TelemetryTimer_Tick;
            Load += MainForm_Load;
            FormClosed += MainForm_FormClosed;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _recenterHotKeyRegistered = RegisterHotKey(Handle, RecenterHotKeyId, ModControl | ModAlt, (uint)Keys.Space);
            if (!_recenterHotKeyRegistered)
            {
                _rendererLabel.Text = "Renderer: D3D11 live panel; recenter button available (hotkey unavailable)";
            }

            if (_settingsStore.LastLoadError != null)
            {
                _captureLabel.Text = "Settings: defaults loaded - " + _settingsStore.LastLoadError;
            }
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            string error;
            if (!_poseSource.TryConnect(out error))
            {
                _connectionLabel.Text = "Air: unavailable - " + error;
                return;
            }

            _connectionLabel.Text = "Air: connected";
            _connectButton.Enabled = false;
            _disconnectButton.Enabled = true;
            _recenterButton.Enabled = true;
            _telemetryTimer.Start();
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            _telemetryTimer.Stop();
            _poseSource.Disconnect();
            _connectionLabel.Text = "Air: disconnected";
            _connectButton.Enabled = true;
            _disconnectButton.Enabled = false;
            _recenterButton.Enabled = false;
            _poseLabel.Text = "Pose: no sample";
        }

        private void RecenterButton_Click(object sender, EventArgs e)
        {
            RecenterCurrentPose();
        }

        private void RecenterCurrentPose()
        {
            PoseSample sample;
            if (_poseSource.TryGetLatest(out sample))
            {
                _posePipeline.Recenter(sample);
                _poseLabel.Text = "Pose: recentered";
            }
            else
            {
                _poseLabel.Text = "Pose: recenter unavailable - " + _poseSource.LastError;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey && message.WParam.ToInt32() == RecenterHotKeyId)
            {
                RecenterCurrentPose();
            }

            base.WndProc(ref message);
        }

        private static void ConfigureNumericInput(NumericUpDown input, Point location, float value, decimal minimum, decimal maximum)
        {
            input.DecimalPlaces = 1;
            input.Increment = 0.1m;
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = Math.Min(maximum, Math.Max(minimum, (decimal)value));
            input.Location = location;
            input.Size = new Size(80, 24);
        }

        private void PanelSetting_ValueChanged(object sender, EventArgs e)
        {
            ApplyPanelSettingsFromControls();
        }

        private bool ApplyPanelSettingsFromControls()
        {
            PanelSettings panelSettings = new PanelSettings
            {
                PanelWidthMeters = (float)_panelWidthInput.Value,
                PanelHeightMeters = (float)_panelHeightInput.Value,
                PanelDistanceMeters = (float)_panelDistanceInput.Value,
                CurvatureRadiusMeters = (float)_curvatureRadiusInput.Value
            };

            try
            {
                panelSettings.Validate();
                PosePipelineSettings poseSettings = _viewerSettings.Pose.Clone();
                poseSettings.HorizonLock = _horizonLockCheckBox.Checked;
                poseSettings.RollLock = _rollLockCheckBox.Checked;
                poseSettings.Validate();
                _viewerSettings.Panel = panelSettings;
                _viewerSettings.Pose = poseSettings;
                _posePipeline.UpdateSettings(poseSettings);
                if (_viewerSession != null)
                {
                    _viewerSession.UpdatePanelSettings(panelSettings);
                }

                return true;
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Settings: " + exception.Message;
                return false;
            }
        }

        private static bool SelectDisplay(ComboBox combo, string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            for (int i = 0; i < combo.Items.Count; i++)
            {
                DisplayInfo display = combo.Items[i] as DisplayInfo;
                if (display != null && string.Equals(display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private void EnsureDistinctDisplaySelection()
        {
            DisplayInfo outputDisplay = _outputDisplayCombo.SelectedItem as DisplayInfo;
            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            if (outputDisplay == null || sourceDisplay == null || !string.Equals(outputDisplay.DeviceName, sourceDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            for (int i = 0; i < _sourceDisplayCombo.Items.Count; i++)
            {
                DisplayInfo candidate = _sourceDisplayCombo.Items[i] as DisplayInfo;
                if (candidate != null && !string.Equals(candidate.DeviceName, outputDisplay.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _sourceDisplayCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SaveViewerSettings()
        {
            ApplyPanelSettingsFromControls();
            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            DisplayInfo outputDisplay = _outputDisplayCombo.SelectedItem as DisplayInfo;
            _viewerSettings.SourceDisplayName = sourceDisplay == null ? null : sourceDisplay.DeviceName;
            _viewerSettings.OutputDisplayName = outputDisplay == null ? null : outputDisplay.DeviceName;
            _settingsStore.Save(_viewerSettings);
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            PoseSample sample;
            Quaternion orientation;
            if (!_poseSource.TryGetLatest(out sample))
            {
                _connectionLabel.Text = "Air: connected, pose unavailable - " + _poseSource.LastError;
                return;
            }

            if (_viewerSession == null)
            {
                _posePipeline.TryProcess(sample, out orientation);
            }
            else
            {
                orientation = Quaternion.Identity;
            }
            _poseLabel.Text = string.Format(
                "Pose: x={0:0.000}, y={1:0.000}, z={2:0.000}, w={3:0.000}",
                orientation.X,
                orientation.Y,
                orientation.Z,
                orientation.W);
        }

        private void LoadDisplays()
        {
            try
            {
                _displays = DisplayEnumerator.Enumerate();
                _logger.Information("display.enumerated", "Windows display count=" + _displays.Count + ".");
                _outputDisplayCombo.Items.Clear();
                _sourceDisplayCombo.Items.Clear();
                for (int i = 0; i < _displays.Count; i++)
                {
                    _outputDisplayCombo.Items.Add(_displays[i]);
                    _sourceDisplayCombo.Items.Add(_displays[i]);
                }

                if (_outputDisplayCombo.Items.Count > 0)
                {
                    if (!SelectDisplay(_outputDisplayCombo, _viewerSettings.OutputDisplayName))
                    {
                        _outputDisplayCombo.SelectedIndex = 0;
                    }

                    if (!SelectDisplay(_sourceDisplayCombo, _viewerSettings.SourceDisplayName))
                    {
                        _sourceDisplayCombo.SelectedIndex = 0;
                    }

                    EnsureDistinctDisplaySelection();
                }
            }
            catch (Exception exception)
            {
                _displayLabel.Text = "Output monitor unavailable: " + exception.Message;
                _outputTestButton.Enabled = false;
                _captureProbeButton.Enabled = false;
            }
        }

        private void CaptureProbeButton_Click(object sender, EventArgs e)
        {
            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            if (sourceDisplay == null)
            {
                return;
            }

            try
            {
                if (_capture != null)
                {
                    _capture.Dispose();
                    _capture = null;
                }

                _capture = new DesktopDuplicationCapture(sourceDisplay);
                DesktopCaptureResult result = _capture.TryAcquire(100);
                if (result.Status == DesktopCaptureStatus.FrameReady)
                {
                    _captureLabel.Text = string.Format("Capture: {0}x{1} frame ready", result.Frame.Width, result.Frame.Height);
                }
                else if (result.Status == DesktopCaptureStatus.Timeout)
                {
                    _captureLabel.Text = "Capture: no update within 100 ms";
                }
                else
                {
                    _captureLabel.Text = "Capture: " + result.Status + " - " + result.Error;
                }
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Capture: unavailable - " + exception.Message;
                if (_capture != null)
                {
                    _capture.Dispose();
                    _capture = null;
                }
            }
        }

        private void OutputTestButton_Click(object sender, EventArgs e)
        {
            if (_viewerSession != null)
            {
                StopViewer();
                return;
            }

            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            DisplayInfo display = _outputDisplayCombo.SelectedItem as DisplayInfo;
            if (sourceDisplay == null || display == null)
            {
                return;
            }

            if (string.Equals(sourceDisplay.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                _captureLabel.Text = "Viewer: choose different source and output displays.";
                _logger.Warning("viewer.start.rejected", "Source and output displays were identical: " + sourceDisplay.DeviceName + ".");
                return;
            }

            if (!ApplyPanelSettingsFromControls())
            {
                return;
            }

            _viewerSettings.SourceDisplayName = sourceDisplay.DeviceName;
            _viewerSettings.OutputDisplayName = display.DeviceName;

            if (_outputWindow != null)
            {
                _outputWindow.Close();
                _outputWindow = null;
            }

            try
            {
                if (_capture != null)
                {
                    _capture.Dispose();
                    _capture = null;
                }

                _outputWindow = new FullscreenOutputWindow(display);
                _outputWindow.FormClosed += delegate { StopViewer(); };
                _outputWindow.Show(this);
                _viewerSession = new DesktopViewerSession(
                    sourceDisplay,
                    display,
                    _outputWindow.Handle,
                    (uint)Math.Max(1, _outputWindow.ClientSize.Width),
                    (uint)Math.Max(1, _outputWindow.ClientSize.Height),
                    _poseSource,
                    _posePipeline,
                    _viewerSettings.Panel,
                    _logger);
                _viewerSession.StatusChanged += ViewerSession_StatusChanged;
                _viewerSession.Start();
                _outputTestButton.Text = "Stop live desktop";
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Viewer: unavailable - " + exception.Message;
                _logger.Error("viewer.start.failed", "The desktop viewer could not start.", exception);
                StopViewer();
            }
        }

        private void ViewerSession_StatusChanged(string status)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate { _captureLabel.Text = "Viewer: " + status; }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void StopViewer()
        {
            if (_viewerSession != null)
            {
                _viewerSession.StatusChanged -= ViewerSession_StatusChanged;
                _viewerSession.Dispose();
                _viewerSession = null;
            }

            _outputTestButton.Text = "Start live desktop";
            if (_outputWindow != null)
            {
                FullscreenOutputWindow outputWindow = _outputWindow;
                _outputWindow = null;
                if (!outputWindow.IsDisposed)
                {
                    outputWindow.Close();
                }
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _telemetryTimer.Stop();
            if (_recenterHotKeyRegistered)
            {
                UnregisterHotKey(Handle, RecenterHotKeyId);
                _recenterHotKeyRegistered = false;
            }
            StopViewer();
            if (_capture != null)
            {
                _capture.Dispose();
                _capture = null;
            }
            try
            {
                SaveViewerSettings();
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Settings: save failed - " + exception.Message;
                _logger.Error("settings.save.failed", "Viewer settings could not be saved.", exception);
            }
            _logger.Information("application.stop", "PhoenixAirViewer stopped.");
            _poseSource.Dispose();
            _logger.Dispose();
        }

        private static IViewerLogger CreateLogger(ViewerSettings settings)
        {
#if PHOENIX_NO_LOGGING
            return NullViewerLogger.Instance;
#else
            if (settings != null && settings.FileLoggingEnabled)
            {
                try
                {
                    return FileViewerLogger.CreateDefault();
                }
                catch
                {
                    return NullViewerLogger.Instance;
                }
            }

            return NullViewerLogger.Instance;
#endif
        }

    }
}
