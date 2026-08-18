using System;
using System.Numerics;

namespace PhoenixAirViewer.Core
{
    public sealed class PosePipeline
    {
        private readonly object _sync = new object();
        private PosePipelineSettings _settings;
        private Quaternion _neutral = Quaternion.Identity;
        private Quaternion _lastOutput = Quaternion.Identity;
        private bool _hasNeutral;
        private bool _hasOutput;
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

        public void UpdateSettings(PosePipelineSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            settings.Validate();
            lock (_sync)
            {
                _settings = settings.Clone();
            }
        }

        public void Recenter(PoseSample sample)
        {
            lock (_sync)
            {
                Quaternion mapped = PoseMath.MapBasis(sample.Orientation, _settings.SensorToRenderer);
                _neutral = mapped;
                _hasNeutral = true;
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
                        _neutral = mapped;
                    }
                    else
                    {
                        _neutral = Quaternion.Identity;
                    }
                    _hasNeutral = true;
                }

                Quaternion target = Quaternion.Multiply(Quaternion.Inverse(_neutral), mapped);
                target = PoseMath.Normalize(target);
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

                if (_hasOutput && _settings.MaxAngularVelocityDegreesPerSecond > 0.0f && deltaSeconds > 0.0)
                {
                    float maximumAngle = (float)(deltaSeconds * _settings.MaxAngularVelocityDegreesPerSecond * Math.PI / 180.0);
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
