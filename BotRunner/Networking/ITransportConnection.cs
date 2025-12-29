using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotRunner.Networking
{
    /// <summary>
    /// Transport abstraction that can be backed by Photon or a mock implementation.
    /// Keeps RPC routing decoupled from Photon-specific types (Hashtable/EventData).
    /// </summary>
    public interface ITransportConnection
    {
        Task ConnectAsync(CancellationToken ct);
        void Service(); // Equivalent to PhotonPeer.Service()
        void Disconnect();
        bool IsConnected { get; }
        event Action<NetEvent>? EventReceived;
        void SendEvent(byte eventCode, object payload, NetReliability reliability);
    }
}
