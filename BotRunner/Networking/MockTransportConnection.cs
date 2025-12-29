using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BotRunner.Networking
{
    /// <summary>
    /// Mock transport for offline testing. Allows callers to enqueue synthetic Photon events and
    /// service them just like PhotonPeer.Service() would.
    /// </summary>
    public class MockTransportConnection : ITransportConnection
    {
        private readonly ConcurrentQueue<NetEvent> _incoming = new();

        public bool IsConnected { get; private set; }

        public event Action<NetEvent>? EventReceived;

        public Task ConnectAsync(CancellationToken ct)
        {
            IsConnected = true;
            Console.WriteLine("[Transport:Mock] Connected (stub)");
            return Task.CompletedTask;
        }

        public void Service()
        {
            while (_incoming.TryDequeue(out var ev))
            {
                Console.WriteLine($"[Transport:Mock] Deliver injected event code={ev.EventCode} type={ev.Payload?.GetType().Name ?? "null"}");
                EventReceived?.Invoke(ev);
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            Console.WriteLine("[Transport:Mock] Disconnected");
        }

        public void SendEvent(byte eventCode, object payload, NetReliability reliability)
        {
            // In mock mode, just log. This keeps higher layers identical between mock and Photon.
            Console.WriteLine($"[Transport:Mock] Send code={eventCode} reliability={reliability} payloadType={payload?.GetType().Name ?? "null"}");
        }

        public void EnqueueIncoming(NetEvent ev)
        {
            _incoming.Enqueue(ev);
        }

        public void Inject(NetEvent ev)
        {
            Console.WriteLine($"[Transport:Mock] Inject code={ev.EventCode} payloadType={ev.Payload?.GetType().Name ?? "null"} sender={ev.SenderActorId}");
            _incoming.Enqueue(ev);
        }
    }
}
