# Custom RMI Protocol Analysis

## Overview
UberStrike 4.3 utilizes a Custom Remote Method Invocation (RMI) protocol built on top of Photon's generic event system, rather than using Photon's high-level RPC features directly.

## Protocol Stack
1.  **Game Logic Layer**: Calls methods like `GameLogic.Shoot()`.
2.  **RMI Layer**: Serializes this call into a byte array with an Operation Code (OpCode).
3.  **Photon Transport**: Sends the byte array using `SendOperation` (OpCode X).
4.  **Network**: UDP/TCP transmission.

## Findings vs BotRunner
- **BotRunner** currently simulates the **Logical Layer** (Step 1 -> 2).
- It uses `RpcMapping.cs` to map names (e.g., `FpsGameRPC.PositionUpdate`) to byte IDs.
- **Discrepancy**: The current `RpcMapping` uses placeholder IDs (e.g., `1`, `2`, `3`) instead of the actual UberStrike OpCodes (which are typically specific byte values).

## Implications for Phase 2 (Server Emulation)
To build a functional server, we must:
1.  Identify the exact OpCodes used by the retail client (e.g., via wireshark or decompilation).
2.  Update `RpcMapping.cs` to use these authoritative IDs.
3.  Ensure payload serialization matches the `ByteConverter` implementation.
