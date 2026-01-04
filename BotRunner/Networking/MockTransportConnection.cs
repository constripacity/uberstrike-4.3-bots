using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BotRunner.Networking
{
    // Clean mock transport without interpolated strings to avoid parsing issues.
    public class MockTransportConnection : ITransportConnection
    {
        private readonly ConcurrentQueue<NetEvent> _incoming = new();

        public bool IsConnected { get; private set; }

        public event Action<NetEvent>? EventReceived;

        public Task ConnectAsync(CancellationToken ct)
        {
            IsConnected = true;
            BotRunner.Utils.Logger.Info("[Transport:Mock] Connected (stub)");
            return Task.CompletedTask;
        }

        public void Service()
        {
            while (_incoming.TryDequeue(out var ev))
            {
                var typeName = ev.Payload == null ? "null" : ev.Payload.GetType().Name;
                BotRunner.Utils.Logger.Info("[Transport:Mock] Deliver injected event code=" + ev.EventCode + " type=" + typeName);
                EventReceived?.Invoke(ev);
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
            BotRunner.Utils.Logger.Info("[Transport:Mock] Disconnected");
        }

        public void SendEvent(byte eventCode, object payload, NetReliability reliability)
        {
            var payloadType = payload == null ? "null" : payload.GetType().Name;
            BotRunner.Utils.Logger.Debug("[Transport:Mock] Send code=" + eventCode + " reliability=" + reliability + " payloadType=" + payloadType);
        }

        public void EnqueueIncoming(NetEvent ev)
        {
            _incoming.Enqueue(ev);
        }

        public void Inject(NetEvent ev)
        {
            var payloadType = ev.Payload == null ? "null" : ev.Payload.GetType().Name;
            BotRunner.Utils.Logger.Info("[Transport:Mock] Inject code=" + ev.EventCode + " payloadType=" + payloadType + " sender=" + ev.SenderActorId);
            _incoming.Enqueue(ev);
        }
    }
}
