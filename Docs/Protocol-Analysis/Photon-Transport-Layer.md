# Photon Transport Layer

## Role in UberStrike
Photon serves as the transport reliability layer. It handles:
- Connection establishment (UDP/TCP)
- Reliable vs Unreliable delivery
- Event dispatching

## BotRunner Implementation
BotRunner simulates this via `ITransportConnection`.
- **MockTransportConnection**: In-memory queue for testing.
- **Photon3TransportConnection**: Wrapper around real Photon SDK (when enabled).

## Missing Pieces
The current `Photon3TransportConnection` is a skeleton. For online play, it needs:
1.  Real `PhotonPeer` instantiation.
2.  Correct AppID and connection flow.
3.  Handling of Photon's internal state machine (Connect -> JoinLobby -> JoinRoom).
