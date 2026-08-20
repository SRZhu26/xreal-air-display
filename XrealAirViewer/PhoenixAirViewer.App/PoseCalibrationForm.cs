using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PhoenixAirViewer.Core;

namespace PhoenixAirViewer.App
{
    public sealed class PoseCalibrationForm : Form
    {
        private const int SamplesPerStep = 3;
        private readonly LatestPoseStore _poseStore;
        private readonly Label _stepLabel;
        private readonly Label _instructionLabel;
        private readonly Label _sampleLabel;
        private readonly Label _statusLabel;
        private readonly Button _recordButton;
        private readonly Button _applyButton;
        private readonly Button _cancelButton;
        private readonly List<PoseSample>[] _samples;
        private readonly string[] _stepNames =
        {
            "Neutral",
            "Yaw right",
            "Yaw left",
            "Pitch up",
            "Pitch down",
            "Roll right",
            "Roll left"
        };
        private readonly string[] _instructions =
        {
            "Look straight ahead in a relaxed position and keep your head still.",
            "Turn your head about 30 degrees to the right. Keep your torso still and hold that pose.",
            "Turn your head about 30 degrees to the left. Keep your torso still and hold that pose.",
            "Look about 20 degrees upward. Keep your torso still and hold that pose.",
            "Look about 20 degrees downward. Keep your torso still and hold that pose.",
            "Tilt your head so your right ear moves toward your right shoulder. Hold that pose.",
            "Tilt your head so your left ear moves toward your left shoulder. Hold that pose."
        };
        private int _stepIndex;
        private PoseCalibrationResult _result;

        public PoseCalibrationForm(LatestPoseStore poseStore)
        {
            _poseStore = poseStore ?? throw new ArgumentNullException("poseStore");
            _samples = new List<PoseSample>[_stepNames.Length];
            for (int index = 0; index < _samples.Length; index++)
            {
                _samples[index] = new List<PoseSample>();
            }

            Text = "XrealAirViewer - Pose Calibration";
            ClientSize = new Size(620, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _stepLabel = new Label { AutoSize = true, Location = new Point(20, 20), Font = new Font(Font, FontStyle.Bold) };
            _instructionLabel = new Label { AutoSize = false, Location = new Point(20, 58), Size = new Size(570, 52) };
            _sampleLabel = new Label { AutoSize = true, Location = new Point(20, 130) };
            _statusLabel = new Label { AutoSize = false, Location = new Point(20, 165), Size = new Size(570, 45) };
            _recordButton = new Button { Location = new Point(20, 235), Size = new Size(150, 32), Text = "Record sample" };
            _applyButton = new Button { Location = new Point(350, 235), Size = new Size(115, 32), Text = "Apply", Enabled = false };
            _cancelButton = new Button { Location = new Point(475, 235), Size = new Size(115, 32), Text = "Cancel" };
            _recordButton.Click += RecordButton_Click;
            _applyButton.Click += ApplyButton_Click;
            _cancelButton.Click += CancelButton_Click;

            Controls.Add(_stepLabel);
            Controls.Add(_instructionLabel);
            Controls.Add(_sampleLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_recordButton);
            Controls.Add(_applyButton);
            Controls.Add(_cancelButton);
            UpdateStepDisplay();
        }

        public PoseCalibrationResult Result
        {
            get { return _result; }
        }

        private void RecordButton_Click(object sender, EventArgs e)
        {
            PoseSample sample;
            if (!_poseStore.TryRead(out sample) || sample.AgeSeconds(PoseClock.NowTicks()) > 0.5)
            {
                _statusLabel.Text = "No fresh pose sample is available. Connect the Air and wait for the Pose status to update.";
                return;
            }

            _samples[_stepIndex].Add(sample);
            _statusLabel.Text = "Recorded sample " + _samples[_stepIndex].Count + " of " + SamplesPerStep + ".";
            if (_samples[_stepIndex].Count < SamplesPerStep)
            {
                UpdateStepDisplay();
                return;
            }

            if (_stepIndex < _samples.Length - 1)
            {
                _stepIndex++;
                UpdateStepDisplay();
                return;
            }

            CompleteCalibration();
        }

        private void CompleteCalibration()
        {
            try
            {
                string error;
                if (!PoseCalibration.TryCompute(
                        _samples[0],
                        _samples[1],
                        _samples[2],
                        _samples[3],
                        _samples[4],
                        _samples[5],
                        _samples[6],
                        out _result,
                        out error))
                {
                    _statusLabel.Text = "Calibration failed: " + error;
                    return;
                }

                _stepLabel.Text = "Calibration complete";
                _instructionLabel.Text = "Review the fit below, then apply it. This corrects axis orientation and signs; it does not change rotation gain.";
                _sampleLabel.Text = "Maximum measured axis error: " + _result.AxisErrorDegrees.ToString("0.0") + " degrees.";
                _statusLabel.Text = _result.AxisErrorDegrees <= 15.0f
                    ? "The measured axes are consistent."
                    : "The measured axes are not close to orthogonal; applying this result may need another pass.";
                _recordButton.Enabled = false;
                _applyButton.Enabled = true;
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "Calibration failed: " + exception.Message;
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void UpdateStepDisplay()
        {
            _stepLabel.Text = "Step " + (_stepIndex + 1) + " of " + _stepNames.Length + ": " + _stepNames[_stepIndex];
            _instructionLabel.Text = _instructions[_stepIndex];
            _sampleLabel.Text = "Recorded samples: " + _samples[_stepIndex].Count + " of " + SamplesPerStep;
            if (_samples[_stepIndex].Count == 0)
            {
                _statusLabel.Text = "Hold the instructed pose, then record three samples.";
            }
        }
    }
}