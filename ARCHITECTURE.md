# UberStrike 4.3 Architecture & Bot Implementation

## 1. UberStrike 4.3 Actual Architecture

Based on extensive reverse engineering and analysis, the UberStrike 4.3 architecture differs significantly from typical Unity multiplayer games of its era.

### Network Topology
*   **Transport Layer**: Uses Photon as a raw transport layer (UDP/TCP).
*   **Protocol**: Custom Remote Method Invocation (RMI) protocol built on top of Photon, not standard Photon Unity Networking (PUN) RPCs.
*   **Server Model**: Strictly authoritative. Game logic (hit registration, movement validation, spawning) runs on the server.
*   **Missing Components**: The server-side logic is proprietary and not available in public repositories. We only have the client.

### Client-Side Reality
*   **Input**: The client sends input states and requests to the server.
*   **State**: The client receives authoritative state snapshots from the server.
*   **Prediction**: The client performs local prediction for movement to ensure smooth gameplay, but this is corrected by server updates.

## 2. Our Approach

Given the lack of server source code and the requirement to run bots, we have adopted a two-phase strategy.

### Phase 1: Client-Side Bots (Offline/Practice)
**Objective**: create bots that function within the official game client in Offline/Practice modes.

*   **Execution Environment**: Inside the Unity Engine (via DLL injection).
*   **Control Mechanism**:
    *   **Input Emulation**: Simulating mouse and keyboard inputs to drive the standard `LocalPlayer` controller.
    *   **Direct Control**: Hooking into the `GameState` to modify local player vectors directly where necessary (e.g., for precise navigation).
*   **Limitations**: These bots only exist locally. They cannot interact with other players online because the official servers would reject their unauthorized traffic/state if we tried to fake it without a proper handshake, and we cannot host our own games without the server software.

### Phase 2: Server Emulation (Online Multiplayer)
**Objective**: Recreate the server logic to host custom games where our bots can play against each other or human players connecting to our emulated server.

*   **Server Emulator**: A standalone application (likely C#) that implements the UberStrike Custom RMI protocol.
*   **Photon Emulation**: Mocking the Photon handshake to allow the client to connect to `localhost`.
*   **Game Loop**: Implementing the authoritative game loop (physics, damage, scoring) on the server.

## 3. Technical Constraints

*   **Unity Version**: The game was built with **Unity 2017.4.40f1**. Any injected code or asset bundles must be compatible with this version.
*   **Runtime**: The game runs on **.NET 3.5**. Modern C# features (Task, async/await) are not natively available and must be avoided or polyfilled.
*   **Injection**: We use `Reflection` to find game objects and managers at runtime because we cannot link against the game's private assemblies at compile time easily.
*   **Source Code**: We have NO access to the original server source code. All server logic must be inferred from client-side traces and decompilation.

## 4. Component Diagram

```mermaid
graph TD
    subgraph "Unity Game Process (Client)"
        Input[Input Hardware] --> UnityInput[Unity Input System]
        UnityInput --> LocalPlayer[Local Player Controller]
        
        Bot[BotController (Injected)] --> |Simulates| UnityInput
        Bot --> |Reads| GameState[GameState / World]
        Bot --> |Controls| Nav[Navigation / Pathfinding]
        
        LocalPlayer --> Physics[Client Physics / Prediction]
        Physics --> Render[Rendering]
    end

    subgraph "Phase 1: Offline Mode"
        LocalPlayer -- Local Logic --> Physics
    end

    subgraph "Phase 2: Server Emulator"
        Network[Photon Transport] -- RPCs --> Server[Server Emulator]
        Server -- State Updates --> Network
    end
```

## References

*   **Analysis**: See `docs/UberStrike-Network-Analysis.pdf` (Placeholder) for packet captures.
*   **Decompilation**: `Assembly-CSharp.dll` from the game client is the primary source of truth.
