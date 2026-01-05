using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BotRunner.Networking
{
    /// <summary>
    /// Thin abstraction over the Photon client used by UberStrike. The real client pumps networking
    /// roughly every 20ms; this class mirrors that Update() pattern while keeping dependencies light
    /// for the reference project.
    ///
    /// This wrapper is intentionally small:
    /// - ConnectAsync() sets up the Photon peer (stubbed here) and prepares callback hooks.
    /// - Update() mirrors PhotonPeer.Service(), delivering inbound events to the registered handlers.
    /// - Disconnect() cleans up just like the retail client on room exit.
    ///
    /// RPC identifiers are resolved through RpcMapping; nothing is hard-coded here to keep the
    /// sample aligned with multiple client/server builds.
    /// </summary>
    public class PhotonConnection
    {
        private readonly string _endpoint;
        private readonly string _appId;
        private readonly ConcurrentQueue<(string rpcName, byte[] payload)> _incoming = new();

        public PhotonConnection(string endpoint, string appId)
        {
            _endpoint = endpoint;
            _appId = appId;
        }

        public bool IsConnected { get; private set; }

        public event Action<string, byte[]>? RpcReceived;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            // TODO: Replace with real Photon initialization using Photon3Unity3D.dll.
            // The appId is the same value used by the production UberStrike client and must
            // be provided out-of-band.
            BotRunner.Utils.Logger.Info($"[Photon] Connecting to {_endpoint} with AppId {_appId}...");
            IsConnected = true;

            // In the Unity client this is where callbacks are registered on PhotonPeer to forward
            // RaiseEvent / OperationResponse payloads into higher-level RPC dispatch. Here we
            // surface a simple enqueue API so RpcRouter can consume updates via Update().
            return Task.CompletedTask;
        }

        public void Update()
        {
            // In the real client this would call PhotonPeer.Service() to pump inbound/outbound traffic.
            while (_incoming.TryDequeue(out var rpc))
            {
                RpcReceived?.Invoke(rpc.rpcName, rpc.payload);
            }
        }

        public void EnqueueIncoming(string rpcName, byte[] payload)
        {
            // This helper keeps the headless sample testable without a Photon server.
            _incoming.Enqueue((rpcName, payload));
        }

        public void Send(string rpcName, byte[] payload)
        {
            // TODO: Implement Photon raise-event with the appropriate RPC identifier and reliability flags.
            // RpcMapping should be consulted here to translate rpcName -> event code before sending.
            BotRunner.Utils.Logger.Debug($"[Photon] Send RPC {rpcName} ({payload.Length} bytes)");
        }

        public void Disconnect()
        {
            if (!IsConnected)
            {
                return;
            }

            BotRunner.Utils.Logger.Info("[Photon] Disconnecting...");
            IsConnected = false;
        }
    }
}
