# Server Reconstruction Requirements

## Goal
Enable `BotRunner` to act as an authoritative server or for a standalone server to host real clients.

## Requirements
1.  **OpCode Discovery**: Map all client-sent operations to their bytes.
2.  **Payload Reverse-Engineering**: Determine the struct layout of every RMI call.
    - *Current status*: `ByteConverter.cs` has some layouts, but they are inferred.
3.  **State Management**: The server must track projectile physics, collisions, and player states authoritatively.
4.  **Lobby Logic**: Matchmaking and room creation (currently mocked in `BotRunner`).

## Strategy
1.  Use `BotRunner`'s deterministic mode to validate game logic.
2.  Inject `ArchitectureValidator` to verify if incoming payloads match expectations.
3.  Slowly replace Mock transport with Real transport listening on a port.
