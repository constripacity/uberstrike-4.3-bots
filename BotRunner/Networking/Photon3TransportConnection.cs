using System;
using System.Threading;
using System.Threading.Tasks;

namespace BotRunner.Networking
{
    /// <summary>
    /// Skeleton Photon 3 transport. This keeps compile-time dependencies optional while documenting
    /// exactly where PhotonPeer hooks should live. Enable with the PHOTON3 build symbol once the
    /// Photon3Unity3D.dll reference is available.
    /// </summary>
    public class Photon3TransportConnection : ITransportConnection
    {
        private readonly string _endpoint;
        private readonly string _appId;

        public Photon3TransportConnection(string endpoint, string appId)
        {
            _endpoint = endpoint;
            _appId = appId;
        }

        public bool IsConnected { get; private set; }

        public event Action<NetEvent>? EventReceived;

        public Task ConnectAsync(CancellationToken ct)
        {
#if PHOTON3
            // TODO: Instantiate PhotonPeer with UDP connection to _endpoint and AppId _appId.
            // Hook OnEvent/OnOperationResponse to enqueue NetEvent:
            //   EventReceived?.Invoke(new NetEvent(eventData.Code, eventData.Parameters, eventData.Sender));
            // Perform any authentication/join steps needed to enter a room.
            IsConnected = true; // Set once Photon reports a successful connect.
            BotRunner.Utils.Logger.Info($"[Transport:Photon3] Connected to {_endpoint}");
            return Task.CompletedTask;
#else
            IsConnected = false;
            BotRunner.Utils.Logger.Warn("[Transport:Photon3] PHOTON3 symbol not defined; connection not established.");
            return Task.CompletedTask;
#endif
        }

        public void Service()
        {
#if PHOTON3
            // TODO: Call PhotonPeer.Service() to pump network messages.
#endif
        }

        public void Disconnect()
        {
#if PHOTON3
            // TODO: Cleanly disconnect PhotonPeer and raise any necessary callbacks.
#endif
            IsConnected = false;
            BotRunner.Utils.Logger.Info("[Transport:Photon3] Disconnected");
        }

        public void SendEvent(byte eventCode, object payload, NetReliability reliability)
        {
#if PHOTON3
            // TODO: Use PhotonPeer.RaiseEvent(eventCode, payload, new RaiseEventOptions { ... }, SendOptions { Reliability = reliability == NetReliability.Reliable });
#else
            BotRunner.Utils.Logger.Warn($"[Transport:Photon3] PHOTON3 symbol not defined; cannot send code={eventCode}");
#endif
        }
    }
}
