using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
    private const int WmDisplayChange = 0x007E;
    private const int RecenterHotKeyId = 1;
    private const int StopLiveDesktopQHotKeyId = 2;
    private const int StopLiveDesktopCHotKeyId = 3;
        private const int AlignmentPreviewHoldMilliseconds = 250;
    private const int StartupRecenterDelayMilliseconds = 1000;
    private const int StartupRecenterCount = 3;
    private const int StartupYawDriftNegativeSweepHundredths = -25;
    private const int StartupYawDriftPositiveSweepHundredths = 25;
    private const int StartupYawDriftFinalHundredths = -11;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

        private readonly AirPoseSource _poseSource;
        private readonly LatestPoseStore _poseStore;
        private readonly LatestPoseObservationStore _poseObservationStore;
        private readonly PosePollingWorker _poseWorker;
        private readonly PosePipeline _posePipeline;
        private readonly Timer _telemetryTimer;
        private readonly Timer _settingsSaveTimer;
        private readonly Timer _alignmentPreviewTimer;
        private readonly Timer _startupRecenterTimer;
        private readonly Label _connectionLabel;
        private readonly Label _poseLabel;
        private readonly Label _rendererLabel;
        private readonly Button _connectButton;
        private readonly Button _disconnectButton;
        private readonly Button _recenterButton;
        private readonly Button _calibrateButton;
        private readonly ComboBox _outputDisplayCombo;
        private readonly ComboBox _sourceDisplayCombo;
        private readonly Button _outputTestButton;
        private readonly Button _captureProbeButton;
        private readonly Button _poseEvidenceButton;
        private readonly Label _displayLabel;
        private readonly Label _captureLabel;
        private readonly Label _diagnosticLabel;
        private readonly ViewerSettingsStore _settingsStore;
        private readonly ViewerSettings _viewerSettings;
        private readonly IViewerLogger _logger;
        private readonly InteractionDiagnosticRecorder _diagnostics;
        private readonly NumericUpDown _panelWidthInput;
        private readonly NumericUpDown _panelHeightInput;
        private readonly NumericUpDown _panelDistanceInput;
        private readonly NumericUpDown _curvatureRadiusInput;
        private readonly NumericUpDown _curvatureYInput;
        private readonly TrackBar _curvatureRadiusTrackBar;
        private readonly TrackBar _curvatureYTrackBar;
        private readonly Label _curvatureRadiusValueLabel;
        private readonly Label _curvatureYValueLabel;
        private readonly Button _gentleCurveButton;
        private readonly ComboBox _distanceProfileCombo;
        private readonly TrackBar _pitchSensitivityTrackBar;
        private readonly TrackBar _yawSensitivityTrackBar;
        private readonly TrackBar _rollSensitivityTrackBar;
        private readonly TrackBar _translationSensitivityTrackBar;
        private readonly TrackBar _pitchDriftRateTrackBar;
        private readonly TrackBar _yawDriftRateTrackBar;
        private readonly Label _pitchSensitivityValueLabel;
        private readonly Label _yawSensitivityValueLabel;
        private readonly Label _rollSensitivityValueLabel;
        private readonly Label _translationSensitivityValueLabel;
        private readonly Label _pitchDriftRateValueLabel;
        private readonly Label _yawDriftRateValueLabel;
        private readonly CheckBox _rollLockCheckBox;
        private readonly CheckBox _horizonLockCheckBox;
        private IList<DisplayInfo> _displays;
        private FullscreenOutputWindow _outputWindow;
        private DesktopDuplicationCapture _capture;
        private DesktopViewerSession _viewerSession;
        private PoseEvidenceCaptureService _poseEvidenceCapture;
        private PoseEvidenceForm _poseEvidenceForm;
        private bool _recenterHotKeyRegistered;
        private bool _stopLiveDesktopQHotKeyRegistered;
        private bool _stopLiveDesktopCHotKeyRegistered;
        private uint _recenterHotKeyVirtualKey;
        private bool _recenterHotKeyHeld;
        private long _recenterHotKeyDownTicks;
        private bool _recenterButtonHeld;
        private bool _alignmentPreviewActive;
        private bool _suppressNextRecenterClick;
        private long _recenterButtonDownTicks;
        private bool _loadingDistanceProfile;
        private bool _loadingCurvatureRadius;
        private bool _loadingPanelPreset;
        private long _lastAlignmentSampleTicks;
        private bool _isClosing;
        private int _startupRecenterCount;

        public MainForm(bool diagnosticMode = false)
        {
            _settingsStore = ViewerSettingsStore.CreateDefault();
            _viewerSettings = _settingsStore.Load();
            _logger = CreateLogger(_viewerSettings, diagnosticMode);
            _logger.Information(
                "application.start",
                "PhoenixAirViewer starting; architecture=x64; runtime=" + Environment.Version + "; os=" + RuntimeInformation.OSDescription + "; logger=" + (_logger.IsEnabled ? "file" : "none"));
            _poseSource = new AirPoseSource(AirQuaternionLayout.Wxyz, _logger);
            _poseStore = new LatestPoseStore();
            _poseObservationStore = new LatestPoseObservationStore();
            _poseWorker = new PosePollingWorker(_poseSource, _poseStore, _poseObservationStore, _logger, false);
            _posePipeline = new PosePipeline(_viewerSettings.Pose);
            _telemetryTimer = new Timer { Interval = 33 };
            _settingsSaveTimer = new Timer { Interval = 2000 };
            _alignmentPreviewTimer = new Timer { Interval = 50 };
            _startupRecenterTimer = new Timer { Interval = StartupRecenterDelayMilliseconds };
            _connectionLabel = new Label();
            _poseLabel = new Label();
            _rendererLabel = new Label();
            _connectButton = new Button();
            _disconnectButton = new Button();
            _recenterButton = new Button();
            _calibrateButton = new Button();
            _outputDisplayCombo = new ComboBox();
            _sourceDisplayCombo = new ComboBox();
            _outputTestButton = new Button();
            _captureProbeButton = new Button();
            _poseEvidenceButton = new Button();
            _displayLabel = new Label();
            _captureLabel = new Label();
            _diagnosticLabel = new Label();
            _panelWidthInput = new NumericUpDown();
            _panelHeightInput = new NumericUpDown();
            _panelDistanceInput = new NumericUpDown();
            _curvatureRadiusInput = new NumericUpDown();
            _curvatureYInput = new NumericUpDown();
            _curvatureRadiusTrackBar = new TrackBar();
            _curvatureYTrackBar = new TrackBar();
            _curvatureRadiusValueLabel = new Label();
            _curvatureYValueLabel = new Label();
            _gentleCurveButton = new Button();
            _distanceProfileCombo = new ComboBox();
            _pitchSensitivityTrackBar = new TrackBar();
            _yawSensitivityTrackBar = new TrackBar();
            _rollSensitivityTrackBar = new TrackBar();
            _translationSensitivityTrackBar = new TrackBar();
            _pitchDriftRateTrackBar = new TrackBar();
            _yawDriftRateTrackBar = new TrackBar();
            _pitchSensitivityValueLabel = new Label();
            _yawSensitivityValueLabel = new Label();
            _rollSensitivityValueLabel = new Label();
            _translationSensitivityValueLabel = new Label();
            _pitchDriftRateValueLabel = new Label();
            _yawDriftRateValueLabel = new Label();
            _rollLockCheckBox = new CheckBox();
            _horizonLockCheckBox = new CheckBox();

            Text = "XrealAirViewer";
            ClientSize = new Size(760, 880);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(780, 920);
            AutoScroll = true;
            AutoScrollMinSize = new Size(760, 880);

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
            _recenterButton.MouseDown += RecenterButton_MouseDown;
            _recenterButton.MouseUp += RecenterButton_MouseUp;

            _calibrateButton.Location = new Point(410, 135);
            _calibrateButton.Size = new Size(120, 32);
            _calibrateButton.Text = "Calibrate pose";
            _calibrateButton.Click += CalibrateButton_Click;

            _displayLabel.AutoSize = true;
            _displayLabel.Location = new Point(20, 195);
            _displayLabel.Text = "XREAL output:";

            _displayLabel.AutoSize = false;
            _displayLabel.Size = new Size(165, 24);
            _displayLabel.TextAlign = ContentAlignment.MiddleRight;

            Label sourceDisplayLabel = CreateFieldLabel(new Point(20, 235), new Size(165, 24), "Desktop source:");

            _outputDisplayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _outputDisplayCombo.Location = new Point(195, 190);
            _outputDisplayCombo.Size = new Size(280, 24);

            _outputTestButton.Location = new Point(495, 188);
            _outputTestButton.Size = new Size(145, 28);
            _outputTestButton.Text = "Start live desktop";
            _outputTestButton.Click += OutputTestButton_Click;

            _sourceDisplayCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sourceDisplayCombo.Location = new Point(195, 230);
            _sourceDisplayCombo.Size = new Size(280, 24);

            _captureProbeButton.Location = new Point(495, 228);
            _captureProbeButton.Size = new Size(145, 28);
            _captureProbeButton.Text = "Probe capture";
            _captureProbeButton.Click += CaptureProbeButton_Click;

            _poseEvidenceButton.Location = new Point(560, 300);
            _poseEvidenceButton.Size = new Size(145, 28);
            _poseEvidenceButton.Text = "Pose evidence";
            _poseEvidenceButton.Visible = diagnosticMode;
            _poseEvidenceButton.Enabled = false;
            _poseEvidenceButton.Click += PoseEvidenceButton_Click;

            _gentleCurveButton.Location = new Point(365, 417);
            _gentleCurveButton.Size = new Size(100, 28);
            _gentleCurveButton.Text = "Wide monitor";
            _gentleCurveButton.UseVisualStyleBackColor = true;
            _gentleCurveButton.Click += GentleCurveButton_Click;

            Label distanceProfileLabel = new Label { AutoSize = true, Location = new Point(20, 422), Text = "Distance profile:" };
            _distanceProfileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _distanceProfileCombo.Location = new Point(150, 417);
            _distanceProfileCombo.Size = new Size(200, 24);
            for (int index = 0; index < _viewerSettings.DistanceProfiles.Count; index++)
            {
                _distanceProfileCombo.Items.Add(_viewerSettings.DistanceProfiles[index]);
            }
            SelectDistanceProfileItem(_viewerSettings.ActiveDistanceProfile);

            _captureLabel.AutoSize = true;
            _captureLabel.Location = new Point(20, 785);
            _captureLabel.Text = "Capture: not probed";
            _captureLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            _diagnosticLabel.AutoSize = false;
            _diagnosticLabel.AutoEllipsis = true;
            _diagnosticLabel.Location = new Point(20, 810);
            _diagnosticLabel.Size = new Size(700, 20);
            _diagnosticLabel.Text = diagnosticMode ? "Diagnostics: starting" : "Diagnostics: off";
            _diagnosticLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            Label pitchSensitivityLabel = CreateSliderLabel(new Point(20, 465), "Pitch sensitivity (tilt):");
            Label yawSensitivityLabel = CreateSliderLabel(new Point(20, 505), "Yaw sensitivity (turn):");
            Label rollSensitivityLabel = CreateSliderLabel(new Point(20, 545), "Roll sensitivity:");
            Label translationSensitivityLabel = CreateSliderLabel(new Point(20, 585), "Translation (yaw + pitch):");
            Label pitchDriftRateLabel = CreateSliderLabel(new Point(20, 625), "Pitch drift rate (deg/s):");
            Label yawDriftRateLabel = CreateSliderLabel(new Point(20, 665), "Yaw drift rate (deg/s):");
            Label panelWidthLabel = CreateFieldLabel(new Point(20, 300), new Size(90, 24), "Panel width (m):");
            Label panelHeightLabel = CreateFieldLabel(new Point(205, 300), new Size(65, 24), "Height (m):");
            Label panelDistanceLabel = CreateFieldLabel(new Point(365, 300), new Size(75, 24), "Distance (m):");
            Label curvatureRadiusLabel = CreateFieldLabel(new Point(20, 340), new Size(160, 24), "Horizontal curve X:");
            Label curvatureYLabel = CreateFieldLabel(new Point(20, 380), new Size(160, 24), "Vertical curve Y:");

            ConfigureNumericInput(_panelWidthInput, new Point(115, 300), _viewerSettings.Panel.PanelWidthMeters, 0.2m, 10.0m);
            ConfigureNumericInput(_panelHeightInput, new Point(275, 300), _viewerSettings.Panel.PanelHeightMeters, 0.2m, 10.0m);
            ConfigureNumericInput(_panelDistanceInput, new Point(445, 300), _viewerSettings.Panel.PanelDistanceMeters, 0.2m, 20.0m);
            ConfigureNumericInput(_curvatureRadiusInput, new Point(190, 340), _viewerSettings.Panel.CurvatureRadiusXMeters, 0.0m, 20.0m);
            ConfigureNumericInput(_curvatureYInput, new Point(190, 380), _viewerSettings.Panel.CurvatureRadiusYMeters, 0.0m, 20.0m);
            ConfigureCurvatureInput(_curvatureRadiusTrackBar, new Point(365, 333), _viewerSettings.Panel.CurvatureRadiusXMeters);
            ConfigureCurvatureInput(_curvatureYTrackBar, new Point(365, 373), _viewerSettings.Panel.CurvatureRadiusYMeters);
            ConfigureCurvatureValueLabel(_curvatureRadiusValueLabel, new Point(680, 345), _curvatureRadiusTrackBar.Value);
            ConfigureCurvatureValueLabel(_curvatureYValueLabel, new Point(680, 385), _curvatureYTrackBar.Value);
            ConfigureSensitivityInput(_pitchSensitivityTrackBar, new Point(210, 453), _viewerSettings.Pose.PitchSensitivity);
            ConfigureSensitivityInput(_yawSensitivityTrackBar, new Point(210, 493), _viewerSettings.Pose.YawSensitivity);
            ConfigureSensitivityInput(_rollSensitivityTrackBar, new Point(210, 533), _viewerSettings.Pose.RollSensitivity);
            ConfigureSensitivityInput(_translationSensitivityTrackBar, new Point(210, 573), _viewerSettings.Panel.TranslationSensitivity);
            ConfigureDriftRateInput(_pitchDriftRateTrackBar, new Point(210, 613), _viewerSettings.Pose.PitchDriftRateDegreesPerSecond);
            ConfigureDriftRateInput(_yawDriftRateTrackBar, new Point(210, 653), _viewerSettings.Pose.YawDriftRateDegreesPerSecond);
            ConfigureSensitivityValueLabel(_pitchSensitivityValueLabel, new Point(525, 465), _pitchSensitivityTrackBar.Value);
            ConfigureSensitivityValueLabel(_yawSensitivityValueLabel, new Point(525, 505), _yawSensitivityTrackBar.Value);
            ConfigureSensitivityValueLabel(_rollSensitivityValueLabel, new Point(525, 545), _rollSensitivityTrackBar.Value);
            ConfigureSensitivityValueLabel(_translationSensitivityValueLabel, new Point(525, 585), _translationSensitivityTrackBar.Value);
            ConfigureDriftRateValueLabel(_pitchDriftRateValueLabel, new Point(525, 625), _pitchDriftRateTrackBar.Value);
            ConfigureDriftRateValueLabel(_yawDriftRateValueLabel, new Point(525, 665), _yawDriftRateTrackBar.Value);

            _rollLockCheckBox.AutoSize = true;
            _rollLockCheckBox.Location = new Point(365, 705);
            _rollLockCheckBox.Text = "Roll lock";
            _rollLockCheckBox.Checked = _viewerSettings.Pose.RollLock;
            _horizonLockCheckBox.AutoSize = true;
            _horizonLockCheckBox.Location = new Point(455, 705);
            _horizonLockCheckBox.Text = "Horizon lock";
            _horizonLockCheckBox.Checked = _viewerSettings.Pose.HorizonLock;
            _panelWidthInput.ValueChanged += PanelSetting_ValueChanged;
            _panelHeightInput.ValueChanged += PanelSetting_ValueChanged;
            _panelDistanceInput.ValueChanged += PanelSetting_ValueChanged;
            _curvatureRadiusInput.ValueChanged += PanelSetting_ValueChanged;
            _curvatureYInput.ValueChanged += PanelSetting_ValueChanged;
            _curvatureRadiusTrackBar.ValueChanged += CurvatureRadiusSlider_ValueChanged;
            _curvatureYTrackBar.ValueChanged += CurvatureRadiusSlider_ValueChanged;
            _distanceProfileCombo.SelectedIndexChanged += DistanceProfile_SelectedIndexChanged;
            _pitchSensitivityTrackBar.ValueChanged += Sensitivity_ValueChanged;
            _yawSensitivityTrackBar.ValueChanged += Sensitivity_ValueChanged;
            _rollSensitivityTrackBar.ValueChanged += Sensitivity_ValueChanged;
            _translationSensitivityTrackBar.ValueChanged += Sensitivity_ValueChanged;
            _pitchDriftRateTrackBar.ValueChanged += DriftRate_ValueChanged;
            _yawDriftRateTrackBar.ValueChanged += DriftRate_ValueChanged;
            _rollLockCheckBox.CheckedChanged += PanelSetting_ValueChanged;
            _horizonLockCheckBox.CheckedChanged += PanelSetting_ValueChanged;
            LoadDistanceProfileIntoControls(GetActiveDistanceProfile());

            Controls.Add(_connectionLabel);
            Controls.Add(_poseLabel);
            Controls.Add(_rendererLabel);
            Controls.Add(_connectButton);
            Controls.Add(_disconnectButton);
            Controls.Add(_recenterButton);
            Controls.Add(_calibrateButton);
            Controls.Add(_displayLabel);
            Controls.Add(sourceDisplayLabel);
            Controls.Add(_outputDisplayCombo);
            Controls.Add(_outputTestButton);
            Controls.Add(_sourceDisplayCombo);
            Controls.Add(_captureProbeButton);
            Controls.Add(_poseEvidenceButton);
            Controls.Add(panelWidthLabel);
            Controls.Add(panelHeightLabel);
            Controls.Add(panelDistanceLabel);
            Controls.Add(curvatureRadiusLabel);
            Controls.Add(curvatureYLabel);
            Controls.Add(distanceProfileLabel);
            Controls.Add(_panelWidthInput);
            Controls.Add(_panelHeightInput);
            Controls.Add(_panelDistanceInput);
            Controls.Add(_curvatureRadiusInput);
            Controls.Add(_curvatureYInput);
            Controls.Add(_curvatureRadiusTrackBar);
            Controls.Add(_curvatureYTrackBar);
            Controls.Add(_curvatureRadiusValueLabel);
            Controls.Add(_curvatureYValueLabel);
            Controls.Add(_gentleCurveButton);
            Controls.Add(_distanceProfileCombo);
            Controls.Add(pitchSensitivityLabel);
            Controls.Add(yawSensitivityLabel);
            Controls.Add(rollSensitivityLabel);
            Controls.Add(translationSensitivityLabel);
            Controls.Add(pitchDriftRateLabel);
            Controls.Add(yawDriftRateLabel);
            Controls.Add(_pitchSensitivityTrackBar);
            Controls.Add(_yawSensitivityTrackBar);
            Controls.Add(_rollSensitivityTrackBar);
            Controls.Add(_translationSensitivityTrackBar);
            Controls.Add(_pitchDriftRateTrackBar);
            Controls.Add(_yawDriftRateTrackBar);
            Controls.Add(_pitchSensitivityValueLabel);
            Controls.Add(_yawSensitivityValueLabel);
            Controls.Add(_rollSensitivityValueLabel);
            Controls.Add(_translationSensitivityValueLabel);
            Controls.Add(_pitchDriftRateValueLabel);
            Controls.Add(_yawDriftRateValueLabel);
            Controls.Add(_rollLockCheckBox);
            Controls.Add(_horizonLockCheckBox);
            Controls.Add(_captureLabel);
            Controls.Add(_diagnosticLabel);

            LoadDisplays();

            InteractionDiagnosticRecorder diagnostics = null;
            if (diagnosticMode)
            {
                try
                {
                    diagnostics = new InteractionDiagnosticRecorder(this, _logger, GetDiagnosticTargets);
                    _diagnosticLabel.Text = "Diagnostics: enabled; session=" + diagnostics.SessionDirectory;
                }
                catch (Exception exception)
                {
                    _diagnosticLabel.Text = "Diagnostics: unavailable - " + exception.Message;
                    _logger.Error("diagnostic.start.failed", "Interaction diagnostics could not start.", exception);
                }
            }

            _diagnostics = diagnostics;
            if (_diagnostics != null)
            {
                try
                {
                    _poseEvidenceCapture = CreatePoseEvidenceCapture(_diagnostics.SessionDirectory);
                    _poseEvidenceButton.Enabled = true;
                }
                catch (Exception exception)
                {
                    _diagnosticLabel.Text = "Diagnostics: evidence unavailable - " + exception.Message;
                    _logger.Error("evidence.start.failed", "Pose evidence capture could not start.", exception);
                }
            }

            _telemetryTimer.Tick += TelemetryTimer_Tick;
            _settingsSaveTimer.Tick += SettingsSaveTimer_Tick;
            _alignmentPreviewTimer.Tick += AlignmentPreviewTimer_Tick;
            _startupRecenterTimer.Tick += StartupRecenterTimer_Tick;
            _outputDisplayCombo.SelectedIndexChanged += DisplaySelectionChanged;
            _sourceDisplayCombo.SelectedIndexChanged += DisplaySelectionChanged;
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
            FormClosed += MainForm_FormClosed;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            uint modifiers;
            uint virtualKey;
            string hotkeyError;
            if (HotkeySettings.TryParse(_viewerSettings.RecenterHotkey, out modifiers, out virtualKey, out hotkeyError))
            {
                _recenterHotKeyVirtualKey = virtualKey;
                _recenterHotKeyRegistered = RegisterHotKey(Handle, RecenterHotKeyId, modifiers, virtualKey);
            }

            if (!_recenterHotKeyRegistered)
            {
                _rendererLabel.Text = "Renderer: D3D11 live panel; recenter button available (hotkey unavailable: " + (hotkeyError ?? "already registered") + ")";
                _logger.Warning("hotkey.register.failed", hotkeyError ?? "The configured recenter hotkey is already registered.");
            }

            uint stopModifiers = HotkeySettings.ModControl | HotkeySettings.ModAlt;
            _stopLiveDesktopQHotKeyRegistered = RegisterHotKey(Handle, StopLiveDesktopQHotKeyId, stopModifiers, (uint)Keys.Q);
            if (!_stopLiveDesktopQHotKeyRegistered)
            {
                _logger.Warning("hotkey.stop.register.failed", "Ctrl+Alt+Q could not be registered. Win32 error=" + Marshal.GetLastWin32Error() + ".");
            }

            _stopLiveDesktopCHotKeyRegistered = RegisterHotKey(Handle, StopLiveDesktopCHotKeyId, stopModifiers, (uint)Keys.C);
            if (!_stopLiveDesktopCHotKeyRegistered)
            {
                _logger.Warning("hotkey.stop.register.failed", "Ctrl+Alt+C could not be registered. Win32 error=" + Marshal.GetLastWin32Error() + ".");
            }

            if (!_stopLiveDesktopQHotKeyRegistered && !_stopLiveDesktopCHotKeyRegistered)
            {
                _logger.Warning("hotkey.stop.unavailable", "Neither live desktop stop hotkey is available; stop the session from the main window.");
            }

            if (_settingsStore.LastLoadError != null)
            {
                _captureLabel.Text = "Settings: defaults loaded - " + _settingsStore.LastLoadError;
            }

            _alignmentPreviewTimer.Start();
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            RecordDiagnosticFeatureClick("connect-air");
            try
            {
                StartPoseWorker();
            }
            catch (Exception exception)
            {
                _connectionLabel.Text = "Air: unavailable - " + exception.Message;
                _logger.Error("air.worker.start.failed", "The pose polling worker could not start.", exception);
            }
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            RecordDiagnosticFeatureClick("disconnect-air");
            ExitAlignmentPreview("disconnect", false);
            _telemetryTimer.Stop();
            if (!_poseWorker.Stop(3000))
            {
                _logger.Warning("air.worker.stop.timeout", "The pose polling worker did not stop within the shutdown timeout.");
            }

            _poseSource.Disconnect();

            _poseStore.Clear();
            _poseObservationStore.Clear();
            _connectionLabel.Text = "Air: disconnected";
            _connectButton.Enabled = true;
            _disconnectButton.Enabled = false;
            _recenterButton.Enabled = false;
            _poseLabel.Text = "Pose: no sample";
        }

        private void RecenterButton_Click(object sender, EventArgs e)
        {
            if (_suppressNextRecenterClick)
            {
                _suppressNextRecenterClick = false;
                return;
            }

            RecordDiagnosticFeatureClick("recenter");
            RecenterCurrentPose();
        }

        private void StartupRecenterTimer_Tick(object sender, EventArgs e)
        {
            if (_isClosing || _viewerSession == null)
            {
                _startupRecenterTimer.Stop();
                _startupRecenterCount = 0;
                return;
            }

            _startupRecenterCount++;
            int yawDriftHundredths = _startupRecenterCount == 1
                ? StartupYawDriftNegativeSweepHundredths
                : (_startupRecenterCount == 2 ? StartupYawDriftPositiveSweepHundredths : StartupYawDriftFinalHundredths);
            if (_yawDriftRateTrackBar.Value != yawDriftHundredths)
            {
                _yawDriftRateTrackBar.Value = yawDriftHundredths;
            }

            RecordDiagnosticAction(
                "recenter.auto; reason=live-desktop-startup; pass=" + _startupRecenterCount + "/" + StartupRecenterCount +
                "; yawDrift=" + FormatDriftRate(yawDriftHundredths) + ";");
            _logger.Information(
                "pose.recenter.scheduled",
                "Startup recenter pass " + _startupRecenterCount + "/" + StartupRecenterCount +
                " triggered; yaw drift=" + FormatDriftRate(yawDriftHundredths) + ".");
            RecenterCurrentPose(true);

            if (_startupRecenterCount >= StartupRecenterCount)
            {
                _startupRecenterTimer.Stop();
            }
        }

        private void RecenterButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _recenterButtonHeld = true;
                _recenterButtonDownTicks = PoseClock.NowTicks();
            }
        }

        private void RecenterButton_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _recenterButtonHeld = false;
                if (_alignmentPreviewActive)
                {
                    _suppressNextRecenterClick = true;
                    ExitAlignmentPreview("button-release", true);
                }
            }
        }

        private void AlignmentPreviewTimer_Tick(object sender, EventArgs e)
        {
            if (_isClosing)
            {
                return;
            }

            if (_recenterButtonHeld && !_alignmentPreviewActive &&
                PoseClock.SecondsBetween(_recenterButtonDownTicks, PoseClock.NowTicks()) * 1000.0 >= AlignmentPreviewHoldMilliseconds)
            {
                EnterAlignmentPreview("button-hold");
            }

            if (_recenterHotKeyHeld && !_alignmentPreviewActive &&
                PoseClock.SecondsBetween(_recenterHotKeyDownTicks, PoseClock.NowTicks()) * 1000.0 >= AlignmentPreviewHoldMilliseconds)
            {
                EnterAlignmentPreview("hotkey-hold");
            }

            if (_recenterHotKeyHeld &&
                (_recenterHotKeyVirtualKey == 0 || (GetAsyncKeyState((int)_recenterHotKeyVirtualKey) & 0x8000) == 0))
            {
                _recenterHotKeyHeld = false;
                if (_alignmentPreviewActive)
                {
                    ExitAlignmentPreview("hotkey-release", true);
                }
                else
                {
                    RecordDiagnosticFeatureClick("recenter-hotkey");
                    RecenterCurrentPose();
                }
            }

            if (_alignmentPreviewActive && !_recenterButtonHeld && !_recenterHotKeyHeld)
            {
                ExitAlignmentPreview("input-released", true);
            }
        }

        private void EnterAlignmentPreview(string reason)
        {
            if (_alignmentPreviewActive)
            {
                return;
            }

            if (_viewerSession == null)
            {
                _captureLabel.Text = "Alignment preview: start live desktop first.";
                return;
            }

            PosePresentationSnapshot presentation = GetLatestPresentation();
            Quaternion neutral;
            bool hasNeutral = _posePipeline.TryGetNeutral(out neutral);
            PosePipelineSettings settings = _posePipeline.Settings;
            _alignmentPreviewActive = true;
            _viewerSession.UpdatePresentationTransform(Vector3.Zero, true);
            _captureLabel.Text = "ALIGNMENT PREVIEW - BORDER VISIBLE - RELEASE TO COMMIT";
            _logger.Information(
                "alignment.preview.entered",
                "reason=" + reason +
                "; hasNeutral=" + hasNeutral +
                "; neutral=" + (hasNeutral ? DescribeQuaternion(neutral) : "none") +
                "; presentation=" + (presentation == null ? "none" : DescribeQuaternion(presentation.ProcessedOrientation)) +
                "; gains=" + DescribeGains(settings) + ".");
            RecordDiagnosticAction("alignment.preview.entered; reason=" + reason + ";");
        }

        private void ExitAlignmentPreview(string reason, bool commitNeutral)
        {
            if (!_alignmentPreviewActive)
            {
                return;
            }

            _alignmentPreviewActive = false;
            if (_viewerSession != null)
            {
                _viewerSession.UpdatePresentationTransform(Vector3.Zero, false);
            }

            _captureLabel.Text = commitNeutral
                ? "Alignment preview: released and recentered."
                : "Alignment preview: room lock restored.";
            _logger.Information(
                "alignment.preview.exited",
                "reason=" + reason + "; commitNeutral=" + commitNeutral + ".");
            RecordDiagnosticAction("alignment.preview.exited; reason=" + reason + ";");
            if (commitNeutral)
            {
                RecordDiagnosticAction("recenter.auto; reason=alignment-preview-release;");
                RecenterCurrentPose();
            }
        }

        private void CalibrateButton_Click(object sender, EventArgs e)
        {
            RecordDiagnosticFeatureClick("calibrate-pose");
            try
            {
                if (_viewerSession != null)
                {
                    StopViewer();
                }

                StartPoseWorker();
                using (PoseCalibrationForm calibrationForm = new PoseCalibrationForm(_poseStore))
                {
                    if (calibrationForm.ShowDialog(this) != DialogResult.OK || calibrationForm.Result == null)
                    {
                        return;
                    }

                    PosePipelineSettings poseSettings = _viewerSettings.Pose.Clone();
                    poseSettings.SensorToRenderer = calibrationForm.Result.SensorToRenderer;
                    poseSettings.Validate();
                    _viewerSettings.Pose = poseSettings;
                    _posePipeline.UpdateSettings(poseSettings);
                    _posePipeline.Reset();
                    RequestSettingsSave();
                    _poseLabel.Text = "Pose: calibration applied (" + calibrationForm.Result.AxisErrorDegrees.ToString("0.0") + " deg axis error)";
                    _captureLabel.Text = "Calibration: applied; recenter before starting live desktop.";
                }
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Calibration: failed - " + exception.Message;
                _logger.Error("pose.calibration.failed", "Pose calibration could not be applied.", exception);
            }
        }

        private void RecenterCurrentPose()
        {
            RecenterCurrentPose(false);
        }

        private void RecenterCurrentPose(bool keepStartupSequence)
        {
            if (!keepStartupSequence)
            {
                _startupRecenterTimer.Stop();
                _startupRecenterCount = 0;
            }

            PoseSample sample;
            if (_poseStore.TryRead(out sample) && sample.AgeSeconds(PoseClock.NowTicks()) <= 0.5)
            {
                PosePipelineSettings poseSettings = _posePipeline.Settings;
                Quaternion previousNeutral;
                bool hadPreviousNeutral = _posePipeline.TryGetNeutral(out previousNeutral);
                Quaternion mapped = PoseMath.MapBasis(sample.Orientation, poseSettings.SensorToRenderer);
                Quaternion previousRelative = hadPreviousNeutral
                    ? PoseMath.Normalize(Quaternion.Multiply(Quaternion.Inverse(previousNeutral), mapped))
                    : Quaternion.Identity;
                Quaternion estimatedOutput = PoseMath.ApplyAxisSensitivity(
                    previousRelative,
                    poseSettings.PitchSensitivity,
                    poseSettings.YawSensitivity,
                    poseSettings.RollSensitivity);
                float previousOffsetDegrees = RadiansToDegrees(PoseMath.AngularDistanceRadians(Quaternion.Identity, estimatedOutput));
                _logger.Information(
                    "pose.recentered",
                    "sampleAgeMs=" + (sample.AgeSeconds(PoseClock.NowTicks()) * 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    "; hadPreviousNeutral=" + hadPreviousNeutral +
                    "; previousOffsetDeg=" + previousOffsetDegrees.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    "; previousRelative=" + DescribeQuaternion(previousRelative) +
                    "; estimatedOutput=" + DescribeQuaternion(estimatedOutput) +
                    "; gains=" + DescribeGains(poseSettings) + ".");
                _posePipeline.Recenter(sample);
                _poseLabel.Text = "Pose: recentered; cleared offset " + previousOffsetDegrees.ToString("0.0") + " deg";
            }
            else
            {
                string error = _poseWorker.LastError;
                _poseLabel.Text = "Pose: recenter unavailable - " + (string.IsNullOrEmpty(error) ? "no recent valid sample" : error);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey && message.WParam.ToInt32() == RecenterHotKeyId)
            {
                _recenterHotKeyHeld = true;
                _recenterHotKeyDownTicks = PoseClock.NowTicks();
            }
            else if (message.Msg == WmHotKey &&
                (message.WParam.ToInt32() == StopLiveDesktopQHotKeyId || message.WParam.ToInt32() == StopLiveDesktopCHotKeyId))
            {
                if (_viewerSession != null)
                {
                    RecordDiagnosticFeatureClick("stop-live-desktop-hotkey");
                    StopViewer();
                }
            }
            else if (message.Msg == WmDisplayChange)
            {
                RecordDiagnosticAction("display-topology.changed");
                if (_viewerSession != null)
                {
                    StopViewer();
                    _captureLabel.Text = "Viewer: display topology changed; select displays and restart.";
                }

                LoadDisplays();
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

        private static Label CreateSliderLabel(Point location, string text)
        {
            return CreateFieldLabel(location, new Size(180, 24), text);
        }

        private static Label CreateFieldLabel(Point location, Size size, string text)
        {
            return new Label
            {
                AutoSize = false,
                Location = location,
                Size = size,
                Text = text,
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private static void ConfigureCurvatureInput(TrackBar input, Point location, float value)
        {
            input.Minimum = 0;
            input.Maximum = 200;
            input.TickFrequency = 20;
            input.SmallChange = 1;
            input.LargeChange = 10;
            input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, (int)Math.Round(value * 10.0f)));
            input.Location = location;
            input.Size = new Size(300, 32);
        }

        private static void ConfigureCurvatureValueLabel(Label label, Point location, int value)
        {
            label.AutoSize = true;
            label.Location = location;
            label.Text = FormatCurvatureRadius(value);
        }

        private static string FormatCurvatureRadius(int sliderValue)
        {
            return sliderValue == 0 ? "Flat" : (sliderValue / 10.0f).ToString("0.0") + " m";
        }

        private void UpdateCurvatureValueLabel()
        {
            _curvatureRadiusValueLabel.Text = FormatCurvatureRadius(_curvatureRadiusTrackBar.Value);
            _curvatureYValueLabel.Text = FormatCurvatureRadius(_curvatureYTrackBar.Value);
        }

        private void SyncCurvatureSliderFromNumeric()
        {
            if (_loadingCurvatureRadius)
            {
                return;
            }

            _loadingCurvatureRadius = true;
            try
            {
                SyncCurvatureSliderFromNumeric(_curvatureRadiusInput, _curvatureRadiusTrackBar);
                SyncCurvatureSliderFromNumeric(_curvatureYInput, _curvatureYTrackBar);
                UpdateCurvatureValueLabel();
            }
            finally
            {
                _loadingCurvatureRadius = false;
            }
        }

        private static void SyncCurvatureSliderFromNumeric(NumericUpDown numericInput, TrackBar trackBar)
        {
            trackBar.Value = Math.Max(
                trackBar.Minimum,
                Math.Min(
                    trackBar.Maximum,
                    (int)Math.Round((double)numericInput.Value * 10.0)));
        }

        private static float RadiansToDegrees(float radians)
        {
            return radians * 180.0f / (float)Math.PI;
        }

        private static string DescribeQuaternion(Quaternion value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.000},{1:0.000},{2:0.000},{3:0.000})",
                value.X,
                value.Y,
                value.Z,
                value.W);
        }

        private static string DescribeGains(PosePipelineSettings settings)
        {
            return "pitch=" + settings.PitchSensitivity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                ";yaw=" + settings.YawSensitivity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                ";roll=" + settings.RollSensitivity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                ";pitchDriftRate=" + settings.PitchDriftRateDegreesPerSecond.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "deg/s" +
                ";yawDriftRate=" + settings.YawDriftRateDegreesPerSecond.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "deg/s";
        }

            private static string DescribeVector4(Vector4 value)
            {
                return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.000},{1:0.000},{2:0.000},{3:0.000})",
                value.X,
                value.Y,
                value.Z,
                value.W);
            }

        private static void ConfigureSensitivityInput(TrackBar input, Point location, float value)
        {
            input.Minimum = (int)(PosePipelineSettings.MinimumAxisSensitivity * 100.0f);
            input.Maximum = (int)(PosePipelineSettings.MaximumAxisSensitivity * 100.0f);
            input.TickFrequency = 50;
            input.SmallChange = 10;
            input.LargeChange = 50;
            input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, (int)Math.Round(value * 100.0f)));
            input.Location = location;
            input.Size = new Size(300, 32);
        }

        private static void ConfigureSensitivityValueLabel(Label label, Point location, int value)
        {
            label.AutoSize = true;
            label.Location = location;
            label.Text = FormatSensitivityPercent(value);
        }

        private static void ConfigureDriftRateInput(TrackBar input, Point location, float value)
        {
            input.Minimum = (int)(PosePipelineSettings.MinimumDriftRateDegreesPerSecond * 100.0f);
            input.Maximum = (int)(PosePipelineSettings.MaximumDriftRateDegreesPerSecond * 100.0f);
            input.TickFrequency = 100;
            input.SmallChange = 1;
            input.LargeChange = 100;
            input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, (int)Math.Round(value * 100.0f)));
            input.Location = location;
            input.Size = new Size(300, 32);
        }

        private static void ConfigureDriftRateValueLabel(Label label, Point location, int value)
        {
            label.AutoSize = true;
            label.Location = location;
            label.Text = FormatDriftRate(value);
        }

        private static string FormatDriftRate(int hundredths)
        {
            return (hundredths > 0 ? "+" : string.Empty) + (hundredths / 100.0f).ToString("0.00") + " deg/s";
        }

        private static string FormatSensitivityPercent(int value)
        {
            return (value > 0 ? "+" : string.Empty) + value.ToString() + "%";
        }

        private void UpdateSensitivityValueLabels()
        {
            _pitchSensitivityValueLabel.Text = FormatSensitivityPercent(_pitchSensitivityTrackBar.Value);
            _yawSensitivityValueLabel.Text = FormatSensitivityPercent(_yawSensitivityTrackBar.Value);
            _rollSensitivityValueLabel.Text = FormatSensitivityPercent(_rollSensitivityTrackBar.Value);
            _translationSensitivityValueLabel.Text = FormatSensitivityPercent(_translationSensitivityTrackBar.Value);
        }

        private void UpdateDriftRateValueLabels()
        {
            _pitchDriftRateValueLabel.Text = FormatDriftRate(_pitchDriftRateTrackBar.Value);
            _yawDriftRateValueLabel.Text = FormatDriftRate(_yawDriftRateTrackBar.Value);
        }

        private DistanceProfileSettings GetActiveDistanceProfile()
        {
            for (int index = 0; index < _viewerSettings.DistanceProfiles.Count; index++)
            {
                DistanceProfileSettings profile = _viewerSettings.DistanceProfiles[index];
                if (profile != null && string.Equals(profile.Key, _viewerSettings.ActiveDistanceProfile, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return _viewerSettings.DistanceProfiles.Count == 0 ? null : _viewerSettings.DistanceProfiles[0];
        }

        private void SelectDistanceProfileItem(string key)
        {
            for (int index = 0; index < _distanceProfileCombo.Items.Count; index++)
            {
                DistanceProfileSettings profile = _distanceProfileCombo.Items[index] as DistanceProfileSettings;
                if (profile != null && string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    _distanceProfileCombo.SelectedIndex = index;
                    return;
                }
            }

            if (_distanceProfileCombo.Items.Count > 0)
            {
                _distanceProfileCombo.SelectedIndex = 0;
            }
        }

        private void LoadDistanceProfileIntoControls(DistanceProfileSettings profile)
        {
            if (profile == null)
            {
                return;
            }

            _loadingDistanceProfile = true;
            try
            {
                _panelDistanceInput.Value = Math.Min(_panelDistanceInput.Maximum, Math.Max(_panelDistanceInput.Minimum, (decimal)profile.PanelDistanceMeters));
                _pitchSensitivityTrackBar.Value = ToSensitivitySliderValue(profile.PitchSensitivity);
                _yawSensitivityTrackBar.Value = ToSensitivitySliderValue(profile.YawSensitivity);
                _rollSensitivityTrackBar.Value = ToSensitivitySliderValue(profile.RollSensitivity);
                _translationSensitivityTrackBar.Value = ToSensitivitySliderValue(profile.TranslationSensitivity);
                UpdateSensitivityValueLabels();
            }
            finally
            {
                _loadingDistanceProfile = false;
            }
        }

        private static int ToSensitivitySliderValue(float value)
        {
            return Math.Max(
                (int)(PosePipelineSettings.MinimumAxisSensitivity * 100.0f),
                Math.Min(
                    (int)(PosePipelineSettings.MaximumAxisSensitivity * 100.0f),
                    (int)Math.Round(value * 100.0f)));
        }

        private void CaptureActiveDistanceProfile()
        {
            DistanceProfileSettings profile = GetActiveDistanceProfile();
            if (profile == null)
            {
                return;
            }

            profile.PanelDistanceMeters = (float)_panelDistanceInput.Value;
            profile.PitchSensitivity = _pitchSensitivityTrackBar.Value / 100.0f;
            profile.YawSensitivity = _yawSensitivityTrackBar.Value / 100.0f;
            profile.RollSensitivity = _rollSensitivityTrackBar.Value / 100.0f;
            profile.TranslationSensitivity = _translationSensitivityTrackBar.Value / 100.0f;
        }

        private void DistanceProfile_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingDistanceProfile || _distanceProfileCombo.SelectedItem == null)
            {
                return;
            }

            CaptureActiveDistanceProfile();
            DistanceProfileSettings selected = _distanceProfileCombo.SelectedItem as DistanceProfileSettings;
            if (selected == null)
            {
                return;
            }

            _viewerSettings.ActiveDistanceProfile = selected.Key;
            LoadDistanceProfileIntoControls(selected);
            RecordDiagnosticAction("distance-profile.changed; profile=" + selected.Key + ";");
            ApplyPanelSettingsFromControls();
        }

        private void PanelSetting_ValueChanged(object sender, EventArgs e)
        {
            if (_loadingDistanceProfile || _loadingCurvatureRadius || _loadingPanelPreset)
            {
                return;
            }

            if (sender == _curvatureRadiusInput || sender == _curvatureYInput)
            {
                _loadingCurvatureRadius = true;
                try
                {
                    NumericUpDown numericInput = sender == _curvatureRadiusInput ? _curvatureRadiusInput : _curvatureYInput;
                    TrackBar trackBar = sender == _curvatureRadiusInput ? _curvatureRadiusTrackBar : _curvatureYTrackBar;
                    SyncCurvatureSliderFromNumeric(numericInput, trackBar);
                    UpdateCurvatureValueLabel();
                }
                finally
                {
                    _loadingCurvatureRadius = false;
                }
            }
            CaptureActiveDistanceProfile();
            RecordDiagnosticAction("panel-setting.changed");
            ApplyPanelSettingsFromControls();
        }

        private void GentleCurveButton_Click(object sender, EventArgs e)
        {
            PanelSettings preset = PanelSettings.CreateWideCurvedMonitor();
            RecordDiagnosticAction("panel.preset; name=wide-monitor;");
            _loadingPanelPreset = true;
            try
            {
                _panelWidthInput.Value = (decimal)preset.PanelWidthMeters;
                _panelHeightInput.Value = (decimal)preset.PanelHeightMeters;
                _panelDistanceInput.Value = (decimal)preset.PanelDistanceMeters;
                _curvatureRadiusInput.Value = (decimal)preset.CurvatureRadiusXMeters;
                _curvatureYInput.Value = (decimal)preset.CurvatureRadiusYMeters;
                _curvatureRadiusTrackBar.Value = (int)Math.Round(preset.CurvatureRadiusXMeters * 10.0f);
                _curvatureYTrackBar.Value = (int)Math.Round(preset.CurvatureRadiusYMeters * 10.0f);
                _translationSensitivityTrackBar.Value = ToSensitivitySliderValue(preset.TranslationSensitivity);
                UpdateSensitivityValueLabels();
                UpdateCurvatureValueLabel();
            }
            finally
            {
                _loadingPanelPreset = false;
            }

            CaptureActiveDistanceProfile();
            ApplyPanelSettingsFromControls();
        }

        private void CurvatureRadiusSlider_ValueChanged(object sender, EventArgs e)
        {
            if (_loadingCurvatureRadius || _loadingPanelPreset)
            {
                return;
            }

            TrackBar input = sender as TrackBar;
            bool isHorizontal = input == _curvatureRadiusTrackBar;
            NumericUpDown numericInput = isHorizontal ? _curvatureRadiusInput : _curvatureYInput;
            float panelExtent = isHorizontal ? (float)_panelWidthInput.Value : (float)_panelHeightInput.Value;
            float requestedRadius = input.Value / 10.0f;
            if (requestedRadius > 0.0f)
            {
                float minimumRadius = panelExtent * 0.5f + 0.1f;
                requestedRadius = Math.Max(requestedRadius, minimumRadius);
            }

            _loadingCurvatureRadius = true;
            try
            {
                numericInput.Value = (decimal)Math.Min(20.0f, requestedRadius);
                input.Value = requestedRadius <= 0.0f
                    ? 0
                    : Math.Max(1, Math.Min(input.Maximum, (int)Math.Round(requestedRadius * 10.0f)));
                UpdateCurvatureValueLabel();
            }
            finally
            {
                _loadingCurvatureRadius = false;
            }

            RecordDiagnosticAction(
                "panel-curvature.changed; axis=" + (isHorizontal ? "x" : "y") + "; radius=" + requestedRadius.ToString("0.0") + "m;");
            ApplyPanelSettingsFromControls();
        }

        private void Sensitivity_ValueChanged(object sender, EventArgs e)
        {
            if (_loadingDistanceProfile || _loadingPanelPreset)
            {
                return;
            }

            UpdateSensitivityValueLabels();
            CaptureActiveDistanceProfile();
            TrackBar input = sender as TrackBar;
            string axis = input == _pitchSensitivityTrackBar
                ? "pitch"
                : (input == _yawSensitivityTrackBar
                    ? "yaw"
                    : (input == _rollSensitivityTrackBar
                        ? "roll"
                        : "translation"));
            RecordDiagnosticAction(
                "pose-sensitivity.changed; axis=" + axis + "; value=" + input.Value.ToString() + "%;");
            ApplyPanelSettingsFromControls();
        }

        private void DriftRate_ValueChanged(object sender, EventArgs e)
        {
            UpdateDriftRateValueLabels();
            TrackBar input = sender as TrackBar;
            string axis = input == _pitchDriftRateTrackBar ? "pitch" : "yaw";
            RecordDiagnosticAction(
                "drift-rate.changed; axis=" + axis + "; value=" + FormatDriftRate(input.Value) + ";");
            ApplyPanelSettingsFromControls();
        }

        private bool ApplyPanelSettingsFromControls()
        {
            PanelSettings panelSettings = new PanelSettings
            {
                PanelWidthMeters = (float)_panelWidthInput.Value,
                PanelHeightMeters = (float)_panelHeightInput.Value,
                PanelDistanceMeters = (float)_panelDistanceInput.Value,
                CurvatureRadiusXMeters = (float)_curvatureRadiusInput.Value,
                CurvatureRadiusYMeters = (float)_curvatureYInput.Value,
                TranslationSensitivity = _translationSensitivityTrackBar.Value / 100.0f
            };

            try
            {
                panelSettings.Validate();
                PosePipelineSettings poseSettings = _viewerSettings.Pose.Clone();
                poseSettings.HorizonLock = _horizonLockCheckBox.Checked;
                poseSettings.RollLock = _rollLockCheckBox.Checked;
                poseSettings.PitchSensitivity = _pitchSensitivityTrackBar.Value / 100.0f;
                poseSettings.YawSensitivity = _yawSensitivityTrackBar.Value / 100.0f;
                poseSettings.RollSensitivity = _rollSensitivityTrackBar.Value / 100.0f;
                poseSettings.PitchDriftRateDegreesPerSecond = _pitchDriftRateTrackBar.Value / 100.0f;
                poseSettings.YawDriftRateDegreesPerSecond = _yawDriftRateTrackBar.Value / 100.0f;
                poseSettings.Validate();
                CaptureActiveDistanceProfile();
                _viewerSettings.Panel = panelSettings;
                _viewerSettings.Pose = poseSettings;
                _posePipeline.UpdateSettings(poseSettings);
                if (_viewerSession != null)
                {
                    _viewerSession.UpdatePanelSettings(panelSettings);
                }

                RequestSettingsSave();

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
            _settingsSaveTimer.Stop();
        }

        private void DisplaySelectionChanged(object sender, EventArgs e)
        {
            RecordDiagnosticAction("display-selection.changed");
            EnsureDistinctDisplaySelection();
            UpdateDisplaySelectionStatus();
            RequestSettingsSave();
        }

        private void PoseEvidenceButton_Click(object sender, EventArgs e)
        {
            RecordDiagnosticFeatureClick("pose-evidence");
            if (_poseEvidenceCapture == null)
            {
                _diagnosticLabel.Text = "Diagnostics: pose evidence is unavailable.";
                return;
            }

            if (_poseEvidenceForm != null && !_poseEvidenceForm.IsDisposed)
            {
                _poseEvidenceForm.Activate();
                return;
            }

            _poseEvidenceForm = new PoseEvidenceForm(
                _poseStore,
                _poseObservationStore,
                _posePipeline,
                _poseEvidenceCapture,
                GetDiagnosticTargets,
                GetLatestPresentation,
                delegate { return _viewerSession; },
                GetActiveDistanceProfile,
                delegate { return _poseWorker.IsConnected ? "connected" : "disconnected"; },
                delegate { return _poseWorker.LastError; });
            _poseEvidenceForm.FormClosed += delegate { _poseEvidenceForm = null; };
            _poseEvidenceForm.Show(this);
        }

        private void UpdateDisplaySelectionStatus()
        {
            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            DisplayInfo outputDisplay = _outputDisplayCombo.SelectedItem as DisplayInfo;
            if (sourceDisplay != null && outputDisplay != null)
            {
                _captureLabel.Text = "Displays: " + sourceDisplay.DeviceName + " -> " + outputDisplay.DeviceName + " (XREAL output).";
            }
        }

        private void RequestSettingsSave()
        {
            if (_isClosing)
            {
                return;
            }

            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();
        }

        private void SettingsSaveTimer_Tick(object sender, EventArgs e)
        {
            _settingsSaveTimer.Stop();
            try
            {
                SaveViewerSettings();
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Settings: save failed - " + exception.Message;
                _logger.Error("settings.save.failed", "Viewer settings could not be saved.", exception);
            }
        }

        private void TelemetryTimer_Tick(object sender, EventArgs e)
        {
            PoseSample sample;
            Quaternion orientation;
            if (!_poseWorker.IsConnected)
            {
                _recenterButton.Enabled = false;
                string error = _poseWorker.LastError;
                _connectionLabel.Text = _poseWorker.IsRunning
                    ? (string.IsNullOrEmpty(error) ? "Air: connecting" : "Air: unavailable - " + error)
                    : "Air: disconnected";
                return;
            }

            if (!_poseStore.TryRead(out sample))
            {
                _connectionLabel.Text = "Air: connected, waiting for pose";
                return;
            }

            double poseAgeSeconds = sample.AgeSeconds(PoseClock.NowTicks());
            if (poseAgeSeconds > 0.5)
            {
                _connectionLabel.Text = "Air: connected, pose stale";
                _poseLabel.Text = string.Format("Pose: stale ({0:0.0} ms)", poseAgeSeconds * 1000.0);
                return;
            }

            _connectionLabel.Text = "Air: connected";
            _recenterButton.Enabled = true;
            orientation = sample.Orientation;
            PosePresentationSnapshot latestPresentation = null;

            if (_viewerSession == null)
            {
                _posePipeline.TryProcess(sample, out orientation);
            }
            else if (_viewerSession.TryGetLatestPresentation(out latestPresentation))
            {
                orientation = latestPresentation.ProcessedOrientation;
            }
            _poseLabel.Text = string.Format(
                "Pose: x={0:0.000}, y={1:0.000}, z={2:0.000}, w={3:0.000}",
                orientation.X,
                orientation.Y,
                orientation.Z,
                orientation.W);
            LogAlignmentPreviewSample(sample, orientation, latestPresentation);
        }

        private void LogAlignmentPreviewSample(PoseSample sample, Quaternion processedOrientation, PosePresentationSnapshot presentation)
        {
            if (!_alignmentPreviewActive || !_logger.IsEnabled)
            {
                return;
            }

            long nowTicks = PoseClock.NowTicks();
            if (_lastAlignmentSampleTicks != 0 && PoseClock.SecondsBetween(_lastAlignmentSampleTicks, nowTicks) < 0.25)
            {
                return;
            }

            _lastAlignmentSampleTicks = nowTicks;
            PoseObservation observation;
            _poseObservationStore.TryRead(out observation);
            PosePipelineSettings settings = _posePipeline.Settings;
            Quaternion neutral;
            bool hasNeutral = _posePipeline.TryGetNeutral(out neutral);
            Quaternion mapped = PoseMath.MapBasis(sample.Orientation, settings.SensorToRenderer);
            Quaternion relative = hasNeutral
                ? PoseMath.Normalize(Quaternion.Multiply(Quaternion.Inverse(neutral), mapped))
                : Quaternion.Identity;
            DistanceProfileSettings profile = GetActiveDistanceProfile();
            _logger.Information(
                "alignment.preview.sample",
                "sampleAgeMs=" + (sample.AgeSeconds(nowTicks) * 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                "; native=" + (observation == null ? "none" : DescribeVector4(observation.NativeComponents)) +
                "; mapped=" + DescribeQuaternion(mapped) +
                "; neutral=" + (hasNeutral ? DescribeQuaternion(neutral) : "none") +
                "; relative=" + DescribeQuaternion(relative) +
                "; processed=" + DescribeQuaternion(processedOrientation) +
                "; presentation=" + (presentation == null ? "none" : presentation.CameraMode + ";offset=" + DescribeVector4(new Vector4(presentation.WorldOffset, 0.0f))) +
                "; profile=" + (profile == null ? "none" : profile.Key) +
                "; gains=" + DescribeGains(settings) + ".");
        }

        private void LoadDisplays()
        {
            try
            {
                DisplayInfo selectedOutput = _outputDisplayCombo.SelectedItem as DisplayInfo;
                DisplayInfo selectedSource = _sourceDisplayCombo.SelectedItem as DisplayInfo;
                string outputDeviceName = selectedOutput == null ? _viewerSettings.OutputDisplayName : selectedOutput.DeviceName;
                string sourceDeviceName = selectedSource == null ? _viewerSettings.SourceDisplayName : selectedSource.DeviceName;
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
                    if (!SelectDisplay(_outputDisplayCombo, outputDeviceName))
                    {
                        _outputDisplayCombo.SelectedIndex = 0;
                    }

                    if (!SelectDisplay(_sourceDisplayCombo, sourceDeviceName))
                    {
                        bool selectedPrimary = false;
                        for (int index = 0; index < _sourceDisplayCombo.Items.Count; index++)
                        {
                            DisplayInfo candidate = _sourceDisplayCombo.Items[index] as DisplayInfo;
                            if (candidate != null && candidate.IsPrimary &&
                                (outputDeviceName == null || !string.Equals(candidate.DeviceName, outputDeviceName, StringComparison.OrdinalIgnoreCase)))
                            {
                                _sourceDisplayCombo.SelectedIndex = index;
                                selectedPrimary = true;
                                break;
                            }
                        }

                        if (!selectedPrimary)
                        {
                            _sourceDisplayCombo.SelectedIndex = 0;
                        }
                    }

                    EnsureDistinctDisplaySelection();
                    UpdateDisplaySelectionStatus();
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
            RecordDiagnosticFeatureClick("probe-capture");
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
            RecordDiagnosticFeatureClick("live-desktop");
            if (_viewerSession != null)
            {
                StopViewer();
                return;
            }

            LoadDisplays();
            if (!TrySelectXrealOutput())
            {
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
                StartPoseWorker();
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
                    _logger,
                    _poseStore);
                _viewerSession.StatusChanged += ViewerSession_StatusChanged;
                _viewerSession.Start();
                _captureLabel.Text = "Viewer: live desktop; source display unchanged.";
                _startupRecenterTimer.Start();
                _outputTestButton.Text = "Stop live desktop";
            }
            catch (Exception exception)
            {
                _captureLabel.Text = "Viewer: unavailable - " + exception.Message;
                _logger.Error("viewer.start.failed", "The desktop viewer could not start.", exception);
                StopViewer();
            }
        }

        private bool TrySelectXrealOutput()
        {
            DisplayInfo xrealDisplay;
            string detectionReason;
            if (!DisplayEnumerator.TryFindXrealDisplay(_displays, out xrealDisplay, out detectionReason))
            {
                _captureLabel.Text = "Displays: XREAL auto-detection unavailable - " + detectionReason;
                return false;
            }

            if (!SelectDisplay(_outputDisplayCombo, xrealDisplay.DeviceName))
            {
                _captureLabel.Text = "Displays: detected XREAL output is not selectable - " + xrealDisplay.DeviceName;
                return false;
            }

            EnsureDistinctDisplaySelection();
            _captureLabel.Text = "Displays: XREAL output auto-detected as " + xrealDisplay.DeviceName + "; " + detectionReason;
            return true;
        }

        private void StartPoseWorker()
        {
            ConnectPoseSourceOnUiThread();
            if (!_poseWorker.IsRunning)
            {
                _poseStore.Clear();
                _poseObservationStore.Clear();
                _poseWorker.Start();
            }

            _connectionLabel.Text = _poseWorker.IsConnected ? "Air: connected" : "Air: connecting";
            _connectButton.Enabled = false;
            _disconnectButton.Enabled = true;
            _recenterButton.Enabled = false;
            _telemetryTimer.Start();
        }

        private void ConnectPoseSourceOnUiThread()
        {
            if (_poseSource.IsConnected)
            {
                return;
            }

            string error;
            if (!_poseSource.TryConnect(out error))
            {
                throw new InvalidOperationException(error ?? "The Air pose source could not connect.");
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
                BeginInvoke(new Action(delegate
                {
                    _captureLabel.Text = "Viewer: " + status;
                    if (status != null && status.StartsWith("stopped", StringComparison.OrdinalIgnoreCase))
                    {
                        StopViewer();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void StopViewer()
        {
            _startupRecenterTimer.Stop();
            _startupRecenterCount = 0;
            ExitAlignmentPreview("viewer-stop", false);
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

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            RecordDiagnosticAction("application.close.requested");
            _isClosing = true;
            ExitAlignmentPreview("application-close", false);
            _settingsSaveTimer.Stop();
            try
            {
                SaveViewerSettings();
            }
            catch (Exception exception)
            {
                _logger.Error("settings.save.failed", "Viewer settings could not be saved.", exception);
                MessageBox.Show(this, "Viewer settings could not be saved: " + exception.Message, "XrealAirViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _telemetryTimer.Stop();
            _settingsSaveTimer.Stop();
            _alignmentPreviewTimer.Stop();
            _startupRecenterTimer.Stop();
            if (_recenterHotKeyRegistered)
            {
                UnregisterHotKey(Handle, RecenterHotKeyId);
                _recenterHotKeyRegistered = false;
            }
            if (_stopLiveDesktopQHotKeyRegistered)
            {
                UnregisterHotKey(Handle, StopLiveDesktopQHotKeyId);
                _stopLiveDesktopQHotKeyRegistered = false;
            }
            if (_stopLiveDesktopCHotKeyRegistered)
            {
                UnregisterHotKey(Handle, StopLiveDesktopCHotKeyId);
                _stopLiveDesktopCHotKeyRegistered = false;
            }
            if (_poseEvidenceForm != null && !_poseEvidenceForm.IsDisposed)
            {
                _poseEvidenceForm.Close();
                _poseEvidenceForm = null;
            }
            if (_poseEvidenceCapture != null)
            {
                _poseEvidenceCapture.Dispose(5000);
                _poseEvidenceCapture = null;
            }
            StopViewer();
            if (_capture != null)
            {
                _capture.Dispose();
                _capture = null;
            }
            _logger.Information("application.stop", "PhoenixAirViewer stopped.");
            bool poseWorkerStopped = _poseWorker.Stop(3000);
            _poseWorker.Dispose();
            if (poseWorkerStopped)
            {
                _poseSource.Disconnect();
                _poseSource.Dispose();
            }
            else
            {
                _logger.Warning("air.worker.stop.timeout", "The native pose source was left undisposed because its worker did not stop safely.");
            }

            if (_diagnostics != null)
            {
                _diagnostics.Dispose();
            }

            _logger.Dispose();
        }

        private IList<DiagnosticScreenshotTarget> GetDiagnosticTargets()
        {
            List<DiagnosticScreenshotTarget> targets = new List<DiagnosticScreenshotTarget>();
            DisplayInfo sourceDisplay = _sourceDisplayCombo.SelectedItem as DisplayInfo;
            DisplayInfo outputDisplay = _outputDisplayCombo.SelectedItem as DisplayInfo;
            if (sourceDisplay != null)
            {
                targets.Add(new DiagnosticScreenshotTarget("source", sourceDisplay.DeviceName, sourceDisplay.Bounds));
            }

            if (outputDisplay != null)
            {
                Rectangle outputBounds = _outputWindow == null ? outputDisplay.Bounds : _outputWindow.Bounds;
                targets.Add(new DiagnosticScreenshotTarget("output", outputDisplay.DeviceName, outputBounds));
            }

            return targets;
        }

        private void RecordDiagnosticFeatureClick(string feature)
        {
            if (_diagnostics != null)
            {
                _diagnostics.RecordFeatureClick(feature);
            }
        }

        private void RecordDiagnosticAction(string action)
        {
            if (_diagnostics != null)
            {
                _diagnostics.RecordAction(action);
            }
        }

        private PosePresentationSnapshot GetLatestPresentation()
        {
            PosePresentationSnapshot snapshot;
            return _viewerSession != null && _viewerSession.TryGetLatestPresentation(out snapshot) ? snapshot : null;
        }

        private PoseEvidenceCaptureService CreatePoseEvidenceCapture(string sessionDirectory)
        {
            IList<DiagnosticScreenshotTarget> targets = GetDiagnosticTargets();
            List<PoseEvidenceDisplay> displays = new List<PoseEvidenceDisplay>();
            for (int index = 0; index < targets.Count; index++)
            {
                DiagnosticScreenshotTarget target = targets[index];
                if (target.Role == "desktop")
                {
                    continue;
                }

                displays.Add(new PoseEvidenceDisplay
                {
                    Role = target.Role,
                    DeviceName = target.DisplayName,
                    Left = target.Bounds.Left,
                    Top = target.Bounds.Top,
                    Width = target.Bounds.Width,
                    Height = target.Bounds.Height
                });
            }

            PoseEvidenceManifest manifest = new PoseEvidenceManifest
            {
                SchemaVersion = 1,
                SessionId = Path.GetFileName(sessionDirectory),
                CreatedUtc = DateTime.UtcNow,
                ProcessId = Environment.ProcessId,
                Runtime = Environment.Version.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                StopwatchFrequency = System.Diagnostics.Stopwatch.Frequency,
                NativeQuaternionLayout = _poseSource.QuaternionLayout.ToString(),
                SensorToRenderer = new PoseEvidenceQuaternion(_viewerSettings.Pose.SensorToRenderer),
                PitchSensitivity = _viewerSettings.Pose.PitchSensitivity,
                YawSensitivity = _viewerSettings.Pose.YawSensitivity,
                RollSensitivity = _viewerSettings.Pose.RollSensitivity,
                TranslationSensitivity = _viewerSettings.Panel.TranslationSensitivity,
                PitchDriftRateDegreesPerSecond = _viewerSettings.Pose.PitchDriftRateDegreesPerSecond,
                YawDriftRateDegreesPerSecond = _viewerSettings.Pose.YawDriftRateDegreesPerSecond,
                ActiveDistanceProfile = _viewerSettings.ActiveDistanceProfile,
                CameraMode = "world-locked",
                CaptureDelayMilliseconds = PoseEvidenceCaptureService.CaptureDelayMilliseconds,
                Panel = _viewerSettings.Panel.Clone(),
                Displays = displays
            };
            PoseEvidenceSessionWriter writer = new PoseEvidenceSessionWriter(sessionDirectory, manifest);
            return new PoseEvidenceCaptureService(writer, _poseStore, _poseObservationStore, _posePipeline, _logger);
        }

        private static IViewerLogger CreateLogger(ViewerSettings settings, bool forceFileLogging)
        {
#if PHOENIX_NO_LOGGING
            return NullViewerLogger.Instance;
#else
            if (forceFileLogging || (settings != null && settings.FileLoggingEnabled))
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
