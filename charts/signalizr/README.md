# signalizr

Deploys the signalizr gateway, the signal-cli REST wrapper it owns, and an optional demo client.
Each component is an alias of the public `workload` chart.

## Dependency Graph

```mermaid
graph LR
  SignalizrChart([signalizr 0.1.0])
  Workload[workload 1.0.3]

  subgraph Aliases[Workload aliases]
    SignalCli[signalcli]
    Gateway[signalizr]
    Demo[demo]
  end

  SignalizrChart --> SignalCli & Gateway & Demo
  SignalCli & Gateway & Demo --> Workload
```

The `signalcli` alias owns the registered Signal account and persistent state. The `signalizr`
alias runs the Gateway and Receiver roles, while `demo` runs the optional DemoClient role and is
disabled by default.
