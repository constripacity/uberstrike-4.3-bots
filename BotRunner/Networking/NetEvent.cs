using System;

namespace BotRunner.Networking
{
    /// <summary>
    /// Lightweight envelope describing an inbound Photon event. Mirrors the shape of RaiseEvent callbacks
    /// so higher layers can map event codes to RPC names without coupling to Photon types.
    /// </summary>
    public readonly struct NetEvent
    {
        public byte EventCode { get; }
        public object? Payload { get; }
        public int SenderActorId { get; }
        public DateTime ReceivedAtUtc { get; }

        public NetEvent(byte eventCode, object? payload, int senderActorId)
        {
            EventCode = eventCode;
            Payload = payload;
            SenderActorId = senderActorId;
            ReceivedAtUtc = DateTime.UtcNow;
        }
    }
}
