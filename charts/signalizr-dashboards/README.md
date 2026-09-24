# Signalizr Grafana dashboards

This chart publishes Signalizr delivery and SignalCli transport dashboards for Grafana sidecar discovery.

## Chart behaviour

Standalone dashboard JSON files live under `dashboards/`, one per dashboard. The template emits one ConfigMap per file with the `grafana_dashboard` discovery label and the configured Grafana folder annotation.

Set `enabled: false` to render no resources. Datasource UIDs are injected through exact replacement of `{{ .Values.datasources.prometheus }}`. Dashboard JSON is never passed through Helm `tpl`, preserving Grafana legend tokens.

## Install

### Helm

```shell
helm upgrade --install signalizr-dashboards \
  oci://ghcr.io/example/charts/signalizr-dashboards \
  --version 0.1.0 \
  --namespace my-namespace --create-namespace
```

### Argo CD Application

Use a dedicated Application targeting the monitoring namespace and set `dashboardFolder` and the Prometheus datasource UID through `valuesObject`.

The deployment workflow reads the private GitOps target from `GITOPS_REPOSITORY`,
`SIGNALIZR_DASHBOARD_MANIFEST_PATH`, `SIGNALIZR_DASHBOARD_NAMESPACE`, and
`SIGNALIZR_DASHBOARD_ENVIRONMENT` repository variables.

## Configuration

| Value | Default | Purpose |
| --- | --- | --- |
| `enabled` | `true` | Renders dashboard ConfigMaps. |
| `dashboardFolder` | `Signalizr` | Grafana folder annotation. |
| `datasources.prometheus` | `prometheus` | Prometheus datasource UID. |

## Dashboards

| File | Title | Scope |
| --- | --- | --- |
| `signalizr-delivery.json` | Signalizr Delivery | Ingress, persistence, subscriber acknowledgements, and SignalCli receive health |

## Related Projects

- [Signalizr](https://github.com/f2calv/signalizr)
- [Grafana dashboard provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/#dashboards)
