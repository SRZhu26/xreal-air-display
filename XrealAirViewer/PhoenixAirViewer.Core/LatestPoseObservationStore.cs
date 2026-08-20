using System;

namespace PhoenixAirViewer.Core
{
    public sealed class LatestPoseObservationStore
    {
        private readonly object _sync = new object();
        private PoseObservation _latest;
        private bool _hasObservation;

        public void Publish(PoseObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException("observation");
            }

            lock (_sync)
            {
                _latest = observation;
                _hasObservation = true;
            }
        }

        public bool TryRead(out PoseObservation observation)
        {
            lock (_sync)
            {
                observation = _latest;
                return _hasObservation;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _latest = null;
                _hasObservation = false;
            }
        }
    }
}