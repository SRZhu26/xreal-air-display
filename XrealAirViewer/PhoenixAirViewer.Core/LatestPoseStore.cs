using System;

namespace PhoenixAirViewer.Core
{
    public sealed class LatestPoseStore
    {
        private readonly object _sync = new object();
        private PoseSample _latest;
        private bool _hasSample;

        public void Publish(PoseSample sample)
        {
            lock (_sync)
            {
                _latest = sample;
                _hasSample = true;
            }
        }

        public bool TryRead(out PoseSample sample)
        {
            lock (_sync)
            {
                sample = _latest;
                return _hasSample;
            }
        }
    }
}
