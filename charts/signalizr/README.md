# signalizr

Deploys the signalizr gateway, the signal-cli REST wrapper it owns, and an optional demo client.
The wrapper uses the application-specific `signalcli` chart, while the gateway and demo client use
the public `workload` chart directly.

## Dependency Graph

```mermaid
graph LR
  SignalizrChart([signalizr 0.1.0])
  SignalCliChart[signalcli 1.0.0]
  Workload[workload 1.0.3]

  subgraph Components[Components]
    SignalCli[signalcli wrapper]
    Gateway[signalizr]
    Demo[demo]
  end

  SignalizrChart --> SignalCliChart & Gateway & Demo
  SignalCliChart --> SignalCli
  SignalCli & Gateway & Demo --> Workload
```

The `signalcli` dependency owns the registered Signal account and persistent state, including the
stable `signalcli` Service name consumed by the gateway. The `signalizr` alias runs the Gateway and
Receiver roles, while `demo` runs the optional DemoClient role and is disabled by default.
