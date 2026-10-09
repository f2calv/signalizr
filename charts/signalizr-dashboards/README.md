# Signalizr Grafana dashboards

Publishes Signalizr delivery and SignalCli transport dashboards as sidecar-discoverable ConfigMaps.
The [`signalizr`](../signalizr/README.md) application chart bundles it as a `file://` subchart enabled by
`dashboards.enabled`; its version stays fixed at `0.1.0`. Set the values below under the application
chart's `dashboards` key.

## Configuration

| Value | Default | Notes |
| --- | --- | --- |
| `enabled` | `true` | Set `false` to render no dashboards |
| `dashboardFolder` | `Signalizr` | Written to the `grafana_folder` annotation |
| `datasources.prometheus` | `prometheus` | Prometheus datasource UID |

Each file under `dashboards/` becomes a ConfigMap named `grafana-dashboard-<file>` with the
`grafana_dashboard: "1"` label, and the datasource UID is substituted into the JSON. Grafana must
run the dashboard sidecar with `folderAnnotation: grafana_folder` for the folder to apply.

### Default Values

```yaml
# Renders the dashboard ConfigMaps.
enabled: true

# Grafana folder annotation.
dashboardFolder: Signalizr

# Datasource UIDs substituted into the dashboard JSON.
datasources:
  prometheus: prometheus
```

## Dashboards

| File | Title | Scope |
| --- | --- | --- |
| `signalizr-delivery.json` | Signalizr Delivery | Ingress, persistence, subscriber acknowledgements, SignalCli receive health, and inbound and outbound HTTP status codes |

## Related Projects

- [Grafana dashboard provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/#dashboards)
- [kube-prometheus-stack](https://github.com/prometheus-community/helm-charts/tree/main/charts/kube-prometheus-stack)
  runs the Grafana sidecar that discovers these ConfigMaps.
- [signalizr](../signalizr/README.md) deploys the gateway these dashboards observe.
