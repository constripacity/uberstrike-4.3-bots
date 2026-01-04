uberstrike-4.3-bots/
├── BotRunner/ — Bot runner application and source
│   ├── BotRunner.csproj — .NET project definition
│   ├── Program.cs — Application entry point and runtime loop
│   ├── Bot/ — Core bot control components
│   │   ├── BotBrain.cs — Bot state machine and orchestration
│   │   ├── BotCombat.cs — Combat behavior helpers
│   │   ├── BotConfig.cs — Bot parameter configuration
│   │   ├── BotMovement.cs — Movement and navigation helpers
│   │   └── Behaviors/ — Pluggable bot behavior implementations
│   │       ├── ChaseNearestEnemyBehavior.cs — Chase nearest target behavior
│   │       ├── DisengageBehavior.cs — Disengage/retreat behavior
│   │       ├── IBotBehavior.cs — Behavior interface contract
│   │       └── WanderBehavior.cs — Random wandering behavior
│   ├── Config/ — Application and scenario configuration
│   │   ├── AppSettings.cs — App-level settings model
│   │   ├── ScenarioConfig.cs — Scenario configuration model
│   │   ├── appsettings.Local.json — Local configuration overrides
│   │   └── appsettings.json — Default configuration
│   ├── Docs/ — Internal documentation and planning notes
│   │   ├── LoggingPlan.txt — Logging approach notes
│   │   ├── MessageToCline_OptionA.md — Communication draft
│   │   ├── README.Logging.md — Logging reference
│   │   └── TODO_M2.md — Milestone tasks
│   ├── Networking/ — Transport connectors and RPC plumbing
│   │   ├── ITransportConnection.cs — Transport interface
│   │   ├── MockTransportConnection.cs — Mock transport implementation
│   │   ├── NetEvent.cs — Network event types
│   │   ├── NetReliability.cs — Reliability helper
│   │   ├── Payload/ — Payload representations
│   │   │   ├── ByteConverter.cs — Byte conversion helpers
│   │   │   ├── PayloadSchemas.cs — Payload schema definitions
│   │   │   └── ShortVector3.cs — Compact vector struct
│   │   ├── Photon3TransportConnection.cs — Photon v3 transport connector
│   │   ├── PhotonConnection.cs — Photon transport implementation
│   │   ├── RpcMapping.cs — RPC mapping definitions
│   │   ├── RpcRouter.cs — RPC routing logic
│   │   ├── RpcSender.cs — RPC sending utilities
│   │   └── TransportConnectionFactory.cs — Transport factory
│   ├── Scenarios/ — Scenario execution orchestration
│   │   └── ScenarioRunner.cs — Scenario runner
│   ├── State/ — Game and player state models
│   │   ├── MatchState.cs — Match state snapshot
│   │   ├── PlayerSnapshot.cs — Player snapshot data
│   │   ├── PlayerState.cs — Player state model
│   │   ├── PlayerStub.cs — Player stub representation
│   │   └── WorldState.cs — World state model
│   └── Utils/ — Shared utilities
│       ├── Logger.cs — Logging helper
│       ├── RateLimiter.cs — Rate limiting helper
│       └── RunMetrics.cs — Metrics tracking
├── LICENSE — Project license
├── README.md — Repository overview
├── .gitignore — Git ignore rules