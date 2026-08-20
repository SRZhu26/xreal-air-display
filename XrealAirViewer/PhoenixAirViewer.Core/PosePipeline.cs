using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePipeline
    {
        private const float AutoRecenterAngularVelocityThresholdDegreesPerSecond = 0.75f;
        private readonly object _sync = new object();
        private PosePipelineSettings _settings;
        private Quaternion _neutral = Quaternion.Identity;
        private Quaternion _lastOutput = Quaternion.Identity;
        private float _yawDriftOffsetRadians;
        private float _pitchDriftOffsetRadians;
        private bool _hasNeutral;
        private bool _hasOutput;
        private bool _hasAutoRecenterCandidate;
        private Quaternion _autoRecenterCandidate = Quaternion.Identity;
        private long _autoRecenterStartTicks;
        private long _lastTimestampTicks;

        public PosePipeline(PosePipelineSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            settings.Validate();
            _settings = settings.Clone();
        }

        public bool HasNeutral
        {
            get
            {
                lock (_sync)
                {
                    return _hasNeutral;
                }
            }
        }

        public PosePipelineSettings Settings
        {
            get
            {
                lock (_sync)
                {
                    return _settings.Clone();
                }
            }
        }

        public bool TryGetNeutral(out Quaternion neutral)
        {
            lock (_sync)
            {
                neutral = _neutral;
                return _hasNeutral;
            }
        }

        public void UpdateSettings(PosePipelineSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            settings.Validate();
            lock (_sync)
            {
                bool driftRateChanged = Math.Abs(_settings.YawDriftRateDegreesPerSecond - settings.YawDriftRateDegreesPerSecond) > 0.00001f ||
                    Math.Abs(_settings.PitchDriftRateDegreesPerSecond - settings.PitchDriftRateDegreesPerSecond) > 0.00001f;
                _settings = settings.Clone();
                if (driftRateChanged)
                {
                    _yawDriftOffsetRadians = 0.0f;
                    _pitchDriftOffsetRadians = 0.0f;
                }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _neutral = Quaternion.Identity;
                _lastOutput = Quaternion.Identity;
                _yawDriftOffsetRadians = 0.0f;
                _pitchDriftOffsetRadians = 0.0f;
                _hasNeutral = false;
                _hasOutput = false;
                _hasAutoRecenterCandidate = false;
                _autoRecenterCandidate = Quaternion.Identity;
                _autoRecenterStartTicks = 0;
                _lastTimestampTicks = 0;
            }
        }

        public void Recenter(PoseSample sample)
        {
            lock (_sync)
            {
                Quaternion mapped = PoseMath.MapBasis(sample.Orientation, _settings.SensorToRenderer);
                _neutral = mapped;
                _hasNeutral = true;
                _hasAutoRecenterCandidate = false;
                _autoRecenterCandidate = Quaternion.Identity;
                _yawDriftOffsetRadians = 0.0f;
                _pitchDriftOffsetRadians = 0.0f;
                _hasOutput = false;
                _lastTimestampTicks = 0;
            }
        }

        public bool TryProcess(PoseSample sample, out Quaternion orientation)
        {
            lock (_sync)
            {
                Quaternion mapped = PoseMath.MapBasis(sample.Orientation, _settings.SensorToRenderer);
                if (!_hasNeutral)
                {
                    if (_settings.AutoRecenterOnFirstSample)
                    {
                        if (_settings.AutoRecenterDelaySeconds > 0.0f)
                        {
                            if (!_hasAutoRecenterCandidate)
                            {
                                _hasAutoRecenterCandidate = true;
                                _autoRecenterCandidate = mapped;
                                _autoRecenterStartTicks = sample.TimestampTicks;
                            }
                            else
                            {
                                double candidateAgeSeconds = PoseClock.SecondsBetween(_autoRecenterStartTicks, sample.TimestampTicks);
                                float candidateAngularVelocity = PoseMath.AngularVelocityDegreesPerSecond(
                                    _autoRecenterCandidate,
                                    mapped,
                                    candidateAgeSeconds);
                                if (candidateAngularVelocity > AutoRecenterAngularVelocityThresholdDegreesPerSecond)
                                {
                                    _autoRecenterCandidate = mapped;
                                    _autoRecenterStartTicks = sample.TimestampTicks;
                                }
                            }

                            double autoRecenterAgeSeconds = PoseClock.SecondsBetween(_autoRecenterStartTicks, sample.TimestampTicks);
                            if (autoRecenterAgeSeconds < _settings.AutoRecenterDelaySeconds)
                            {
                                _lastOutput = Quaternion.Identity;
                                _lastTimestampTicks = sample.TimestampTicks;
                                _hasOutput = true;
                                orientation = Quaternion.Identity;
                                return true;
                            }
                        }

                        _neutral = mapped;
                        _yawDriftOffsetRadians = 0.0f;
                        _pitchDriftOffsetRadians = 0.0f;
                        _hasAutoRecenterCandidate = false;
                        _autoRecenterCandidate = Quaternion.Identity;
                    }
                    else
                    {
                        _neutral = Quaternion.Identity;
                    }
                    _hasNeutral = true;
                }

                Quaternion target = Quaternion.Multiply(Quaternion.Inverse(_neutral), mapped);
                target = PoseMath.Normalize(target);
                target = PoseMath.ApplyAxisSensitivity(
                    target,
                    _settings.PitchSensitivity,
                    _settings.YawSensitivity,
                    _settings.RollSensitivity);
                if (_settings.HorizonLock)
                {
                    target = PoseMath.RemoveRollAroundForward(target, Vector3.UnitY);
                }
                if (_settings.RollLock)
                {
                    target = PoseMath.RemoveTwistAroundAxis(target, Vector3.UnitZ);
                }

                double deltaSeconds = _hasOutput ? PoseClock.SecondsBetween(_lastTimestampTicks, sample.TimestampTicks) : 0.0;
                if (deltaSeconds < 0.0)
                {
                    deltaSeconds = 0.0;
                }

                if (deltaSeconds > 0.0)
                {
                    _yawDriftOffsetRadians += _settings.YawDriftRateDegreesPerSecond * (float)deltaSeconds * (float)Math.PI / 180.0f;
                    _pitchDriftOffsetRadians += _settings.PitchDriftRateDegreesPerSecond * (float)deltaSeconds * (float)Math.PI / 180.0f;
                    Quaternion driftOffset = Quaternion.Multiply(
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, _yawDriftOffsetRadians),
                        Quaternion.CreateFromAxisAngle(Vector3.UnitX, _pitchDriftOffsetRadians));
                    target = PoseMath.Normalize(Quaternion.Multiply(driftOffset, target));
                }

                float maximumAngularVelocityDegreesPerSecond = _settings.MaxAngularVelocityDegreesPerSecond > 0.0f
                    ? _settings.MaxAngularVelocityDegreesPerSecond
                    : _settings.PoseStabilityLimitDegreesPerSecond;
                if (_hasOutput && maximumAngularVelocityDegreesPerSecond > 0.0f && deltaSeconds > 0.0)
                {
                    float maximumAngle = (float)(deltaSeconds * maximumAngularVelocityDegreesPerSecond * Math.PI / 180.0);
                    target = PoseMath.RotateToward(_lastOutput, target, maximumAngle);
                }

                if (_hasOutput && _settings.SmoothingTimeConstantSeconds > 0.0f && deltaSeconds > 0.0)
                {
                    float smoothingAmount = 1.0f - (float)Math.Exp(-deltaSeconds / _settings.SmoothingTimeConstantSeconds);
                    target = PoseMath.SlerpShortest(_lastOutput, target, smoothingAmount);
                }

                _lastOutput = PoseMath.Normalize(target);
                _lastTimestampTicks = sample.TimestampTicks;
                _hasOutput = true;
                orientation = _lastOutput;
                return true;
            }
        }

    }
}
