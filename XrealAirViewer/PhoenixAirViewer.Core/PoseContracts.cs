using System;

namespace PhoenixAirViewer.Core
{
    public interface IPoseSource : IDisposable
    {
        bool IsConnected { get; }
        string LastError { get; }
        bool TryConnect(out string error);
        void Disconnect();
        bool TryGetLatest(out PoseSample sample);
    }
}
