# Architecture

This drilldown opens signalizr from the [System Context](../README.md#system-context) into its
containers and the components of its two application roles. Project READMEs keep their focused
flows: the [Backend delivery data flow](../src/CasCap.Backend/README.md), the
[Comms event flow](../src/CasCap.Comms/README.md) and the
[Comms.AI responder sequence](../src/CasCap.Comms.AI/README.md).

## Containers

One release image provides every signalizr process; `CasCap:FeatureConfig:EnabledFeatures` selects
its role. The gateway runs `Gateway` and `Receiver` together and is the single owner of the Signal
account. A Comms responder is the same image running only `Comms`: a client of the gateway like any
other application. The [chart](../charts/signalizr/README.md) deploys the wrapper, the gateway and a
migration Job, with dormant DemoClient and Comms responder workloads.

```mermaid
flowchart LR
    classDef container fill:#dbeafe,stroke:#1d4ed8,color:#1e3a8a
    classDef store fill:#e5e7eb,stroke:#4b5563,color:#111827
    classDef person fill:#ede9fe,stroke:#6d28d9,color:#2e1065
    classDef external fill:#fef3c7,stroke:#d97706,color:#78350f

    apps([Consumer applications]):::person -->|"REST send, gRPC subscribe"| gateway
    operator([Operator]):::person -->|"MCP, Basic auth"| gateway
    subgraph signalizr [signalizr]
        wrapper[Signal wrapper<br/>signal-cli-rest-api]:::container
        gateway[Gateway<br/>Gateway and Receiver roles]:::container
        migrate[Migration job<br/>same image, --migrate]:::container
        responder[Comms responder<br/>same image, Comms role]:::container
        demo[DemoClient<br/>same image, optional]:::container
        db[(Gateway database<br/>SQLite or PostgreSQL)]:::store
        accountData[(Signal account state)]:::store
        gateway -->|"HTTP API"| wrapper
        wrapper --> accountData
        gateway -->|"EF Core: messages, attachments, cursors"| db
        migrate -->|schema| db
        responder -->|"gRPC subscribe, REST send"| gateway
        demo -->|"gRPC subscribe, REST send"| gateway
    end
    wrapper <-->|"Signal protocol"| signal[Signal service]:::external
    responder -->|"stream, media, deduplication"| redis[(Redis)]:::external
    responder -->|"streamed agent runs"| runtime[Agent Runtime]:::external
```

PostgreSQL and Redis are supplied by the deployment; the Compose file starts local instances.

## Gateway Components

The gateway separates the single receive loop from everything that may block: the Receiver only
enqueues, and the dispatcher resolves groups, downloads attachments and persists each delivery
before notifying subscribers. Every call to Signal goes through the wrapper's HTTP API.

```mermaid
flowchart TB
    classDef component fill:#dbeafe,stroke:#1d4ed8,color:#1e3a8a
    classDef store fill:#e5e7eb,stroke:#4b5563,color:#111827
    classDef external fill:#fef3c7,stroke:#d97706,color:#78350f

    subgraph gateway [Gateway]
        rest[REST endpoints<br/>/api/v1/groups, attachments]:::component
        grpc[gRPC subscriptions<br/>Inbound.Subscribe]:::component
        mcp[MCP operator tools]:::component
        send[Message gateway<br/>IMessageGateway]:::component
        groups[Group resolution<br/>IGroupResolver]:::component
        receiver[Receiver<br/>IInboundMessageQueue]:::component
        dispatch[Dispatch and persistence]:::component
        registry[Subscriber registry<br/>IInboundSubscriberRegistry]:::component
        notices[Operator notices<br/>IOperatorNotifier]:::component
        data[Persistence model<br/>SignalizrDbContext]:::component

        rest --> send
        rest --> groups
        rest -->|attachments| dispatch
        grpc --> registry
        grpc --> notices
        mcp --> groups
        mcp --> registry
        mcp -->|"optional send"| send
        mcp -->|"optional history"| data
        send --> groups
        send --> notices
        dispatch -->|IInboundMessageQueue| receiver
        dispatch --> groups
        dispatch -->|notify| registry
        dispatch --> data
        registry --> data
        registry --> notices
    end
    send --> wrapper[Signal wrapper]:::external
    groups --> wrapper
    receiver --> wrapper
    dispatch -->|"attachment download"| wrapper
    notices --> wrapper
    data --> db[(Gateway database)]:::store
```

## Gateway Code Map

Gateway components span three projects: endpoints in `CasCap.App.Server`, behavior in
`CasCap.Backend` and the EF Core model in `CasCap.Data`.

| Component | Responsibility | Code location | Provides | Requires |
| --- | --- | --- | --- | --- |
| REST endpoints | Named-group send, reactions, typing, polls and attachment download | `CasCap.App.Server/Controllers/`, `Services/GroupsApiExceptionHandler.cs` | `/api/v1/groups`, attachment route | `IMessageGateway`, `IGroupResolver`, `InboundAttachmentService` |
| gRPC subscriptions | Bidirectional delivery stream with acknowledgements and timeouts | `CasCap.App.Server/Services/InboundGrpcService.cs`, `CasCap.Signalizr.Client/Protos/inbound.proto` | `Inbound.Subscribe` | `IInboundSubscriberRegistry`, `IOperatorNotifier` |
| MCP operator tools | Status, group list, optional history and optional send | `CasCap.App.Server/Services/SignalizrMcp*.cs`, `Extensions/McpServiceExtensions.cs` | `/mcp` tools and prompts | `IGroupResolver`, `IInboundSubscriberRegistry`, `IMessageGateway`, `SignalizrDbContext` |
| Message gateway | Sends messages, reactions, typing leases and polls to exact group names | `CasCap.Backend/Services/MessageGateway.cs`, `TypingLeaseService.cs` | `IMessageGateway` | `IGroupResolver`, `IOperatorNotifier`, wrapper |
| Group resolution | Maps configured group names to wrapper group identifiers, retrying while the wrapper is down | `CasCap.Backend/Services/GroupResolver.cs`, `GatewayBgService.cs` | `IGroupResolver` | Wrapper |
| Receiver | Single owner of the wrapper receive stream; only enqueues into a drop-oldest queue | `CasCap.Backend/Services/ReceiverBgService.cs`, `InboundMessageQueue.cs` | `IInboundMessageQueue` | Wrapper |
| Dispatch and persistence | Maps envelopes to deliveries, stores attachments and messages, prunes retention | `CasCap.Backend/Services/DispatcherBgService.cs`, `InboundMessagePersistenceService.cs`, `InboundAttachmentService.cs`, `DurableMessagePrunerService.cs` | `InboundAttachmentService` | `IInboundMessageQueue`, `IGroupResolver`, `IInboundSubscriberRegistry`, `SignalizrDbContext`, wrapper |
| Subscriber registry | One live stream per subscriber identity, durable cursors and per-group fan-out | `CasCap.Backend/Services/InboundSubscriberRegistry.cs`, `InboundSubscription.cs` | `IInboundSubscriberRegistry` | `SignalizrDbContext`, `IOperatorNotifier` |
| Operator notices | Bounded queue of operational notices sent to the operator group | `CasCap.Backend/Services/OperatorNotifier.cs` | `IOperatorNotifier` | Wrapper |
| Persistence model | Entities, context and migrations for messages, attachments and cursors | `CasCap.Data/`, `CasCap.Backend/Extensions/DataServiceCollectionExtensions.cs` | `SignalizrDbContext` | SQLite or PostgreSQL |

## Comms Responder Components

The Comms responder is assembled from the published `CasCap.Comms` and `CasCap.Comms.AI` packages,
which other hosts also consume. Event producers in other applications write to the Redis stream through
the package's stream sink; the responder only reads it.

```mermaid
flowchart TB
    classDef component fill:#dbeafe,stroke:#1d4ed8,color:#1e3a8a
    classDef external fill:#fef3c7,stroke:#d97706,color:#78350f

    subgraph responder [Comms responder]
        pipeline[Comms pipeline<br/>CommunicationsBgService]:::component
        dedup[Deduplication<br/>ISignalMessageDeduplicator]:::component
        routing[Event formatting and routing<br/>ICommsEventFormatter, ICommsGroupRouter]:::component
        voice[Voice<br/>IVoiceTranscriptionService, IVoiceSynthesisService]:::component
        agent[Agent responder<br/>ICommsResponder]:::component
        polls[Poll tracking<br/>IPollTracker]:::component
        debug[Debug notices]:::component
        client[Signalizr client<br/>ISignalizrClient]:::component

        pipeline --> dedup
        pipeline --> routing
        pipeline --> voice
        pipeline -->|ICommsResponder| agent
        pipeline --> client
        agent --> polls
        agent --> debug
        agent --> client
        debug --> client
    end
    pipeline -->|"comms stream"| redis[(Redis)]:::external
    dedup --> redis
    voice --> speech[Speech providers]:::external
    agent -->|IAgentRuntimeClient| runtime[Agent Runtime]:::external
    client -->|"gRPC subscribe, REST send"| gateway[Gateway]:::external
```

## Comms Responder Code Map

| Component | Responsibility | Code location | Provides | Requires |
| --- | --- | --- | --- | --- |
| Comms pipeline | Reads the event stream and gateway deliveries, filters, throttles and drains a bounded reply queue | `CasCap.Comms/Services/CommunicationsBgService*.cs` | `Comms` background feature | All components below, Redis |
| Deduplication | Suppresses duplicate deliveries by hashed message identity | `CasCap.Comms/Services/RedisSignalMessageDeduplicator.cs` | `ISignalMessageDeduplicator` | Redis |
| Event formatting and routing | Formats stream events and routes monitor sources to the monitor group | `CasCap.Comms/Services/*CommsEventFormatter.cs`, `MonitorSourcesGroupRouter.cs` | `ICommsEventFormatter`, `ICommsGroupRouter` | None |
| Voice | Transcribes voice notes and synthesizes spoken replies | `CasCap.Api.Voice` package | `IVoiceTranscriptionService`, `IVoiceSynthesisService` | Speech providers |
| Agent responder | Runs streamed Agent Runtime turns and slash commands, producing replies | `CasCap.Comms.AI/Services/AgentCommsResponder*.cs`, `Extensions/CommsAgentCommandParser.cs` | `ICommsResponder` | `IAgentRuntimeClient`, `ISignalizrClient`, `IPollTracker`, Debug notices |
| Poll tracking | Remembers active polls so votes can become responder turns | `CasCap.Comms.AI/Services/InMemoryPollTracker.cs` | `IPollTracker` | None |
| Debug notices | Posts run statistics and diagnostics to the monitor group when enabled | `CasCap.Comms.AI/Services/CommsDebugNotifier.cs` | Debug footer and notices | `ISignalizrClient` |
| Signalizr client | Typed REST and gRPC client for the gateway | `CasCap.Signalizr.Client/` | `ISignalizrClient` | Gateway |

## Runtime Scenarios

### Receive and Deliver a Message

Delivery is at least once after persistence. A subscriber that stops acknowledging is disconnected;
on reconnection it resumes after its last contiguous acknowledgement.

```mermaid
sequenceDiagram
    autonumber
    participant app as Consumer application
    participant grpc as gRPC subscriptions
    participant registry as Subscriber registry
    participant wrapper as Signal wrapper
    participant receiver as Receiver
    participant dispatch as Dispatch and persistence
    participant db as Gateway database

    app->>grpc: Hello with subscriber name
    grpc->>registry: SubscribeAsync
    alt identity already connected
        grpc-->>app: AlreadyExists
    else subscribed
        wrapper-)receiver: received envelope
        receiver-)dispatch: IInboundMessageQueue
        dispatch->>wrapper: download attachments
        dispatch->>db: commit message and attachments
        dispatch->>registry: NotifyMessageAvailable
        registry->>db: read messages after cursor
        grpc->>app: InboundMessage
        alt acknowledged within timeout
            app->>grpc: Ack
            grpc->>registry: AcknowledgeAsync
            registry->>db: advance cursor
        else no acknowledgement
            grpc-->>app: DeadlineExceeded, disconnect
        end
    end
```
