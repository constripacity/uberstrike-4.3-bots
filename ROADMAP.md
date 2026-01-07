# Roadmap

## Phase 1: Client-Side Offline Bots (Current Focus)
**Goal:** Create intelligent bots that can play effectively in the "Offline Practice" mode of UberStrike 4.3.

- [x] **Injection Mechanism**: Ability to inject C# code into the running Unity process.
- [x] **State Reading**: Accessing player position, health, ammo, and enemy positions via Reflection.
- [ ] **Basic Movement**: Implementing pathfinding (A*) or navmesh integration for complex map navigation.
- [ ] **Combat Logic**: Aimbot, trigger bot, and weapon selection logic.
- [ ] **Behavior Trees**: Implementing `Wander`, `Chase`, `Cover`, and `Attack` behaviors.
- [ ] **Visual Debugging**: Drawing lines and gizmos in the game world to visualize bot thinking.

## Phase 2: Server Emulation (Future)
**Goal:** Reverse engineer the server protocol to allow custom multiplayer matches.

- [ ] **Protocol Analysis**: Documenting the specific opcodes and serialization format of the Custom RMI.
- [ ] **Handshake Mocking**: Creating a fake Photon server that the client accepts as legitimate.
- [ ] **Lobby System**: Basic room creation and joining.
- [ ] **Game Loop Implementation**: Recreating the core deathmatch rules on the server.
- [ ] **Bot Hosting**: Running headless instances of the bots on the server side.

## Phase 3: Advanced Features
- [ ] **Machine Learning**: Training bots using reinforcement learning (requires the fast simulation of Phase 2).
- [ ] **Tournament System**: Automated matchmaking and ranking for bot vs bot battles.
