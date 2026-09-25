# Signalizr Grafana dashboards

Publishes Signalizr delivery and SignalCli transport dashboards as sidecar-discoverable ConfigMaps.
The chart is independent of the [`signalizr`](../signalizr/README.md) application chart and is
versioned by its own `Chart.yaml`. It is published to
`oci://ghcr.io/f2calv/charts/signalizr-dashboards`.

## Install

### Helm

Install the dashboards into the namespace your Grafana sidecar watches:

```bash
helm install signalizr-dashboards oci://ghcr.io/f2calv/charts/signalizr-dashboards --version 0.1.1 \
  --namespace my-namespace --create-namespace \
  --set-string datasources.prometheus=prometheus
```

Upgrade to the latest stable chart published in GHCR:

```bash
helm upgrade --install signalizr-dashboards oci://ghcr.io/f2calv/charts/signalizr-dashboards \
  --namespace my-namespace --create-namespace \
  --set-string datasources.prometheus=prometheus
```

### Argo CD Application

[Argo CD](https://argo-cd.readthedocs.io/) can consume the same OCI package directly:

```yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: signalizr-dashboards
  namespace: argocd
spec:
  project: default
  destination:
    namespace: my-namespace
    server: https://kubernetes.default.svc
  source:
    repoURL: ghcr.io/f2calv
    chart: charts/signalizr-dashboards
    targetRevision: 0.1.1
    helm:
      valuesObject:
        dashboardFolder: Signalizr
        datasources:
          prometheus: prometheus
  syncPolicy:
    automated:
      prune: true
      selfHeal: true
```

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
| `signalizr-delivery.json` | Signalizr Delivery | Ingress, persistence, subscriber acknowledgements, and SignalCli receive health |

## Related Projects

- [Grafana dashboard provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/#dashboards)
- [kube-prometheus-stack](https://github.com/prometheus-community/helm-charts/tree/main/charts/kube-prometheus-stack)
  runs the Grafana sidecar that discovers these ConfigMaps.
- [signalizr](../signalizr/README.md) deploys the gateway these dashboards observe.