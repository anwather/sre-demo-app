# SRE Demo API (Project 2)

An independently publishable .NET 8 minimal API for AKS SRE demonstrations. It writes
operations to private Azure Blob Storage through AKS Workload Identity, emits structured
logs and OpenTelemetry to Application Insights, and supports bounded fault injection that
is disabled by default.

## What is included

- `GET /healthz`: process liveness only.
- `GET /ready`: checks that the configured Blob container is reachable.
- `POST /api/operations`: stores a JSON operation in Blob Storage.
- `GET /api/operations/{id}`: reads a stored operation.
- JSON console logs with operation IDs and fault-mode fields.
- ASP.NET Core request, dependency, exception, trace, custom activity, and custom metric
  telemetry through `Azure.Monitor.OpenTelemetry.AspNetCore`.
- Non-root, multi-stage .NET 8 container with a built-in Docker health check.
- AKS Workload Identity manifests, two replicas, ClusterIP Service, probes, resource
  limits, topology spreading, and PodDisruptionBudget.

## Prerequisites

Use a connected administration host that can resolve and reach the private AKS API,
private ACR endpoint, and private Blob endpoint. Install:

- .NET 8 SDK
- Docker
- Azure CLI
- `kubectl`
- Optional: Trivy for local image scanning

Azure prerequisites:

1. AKS has OIDC issuer and Workload Identity enabled.
2. A user-assigned managed identity exists for this application.
3. That identity has `Storage Blob Data Contributor` scoped to the target storage account
   or container.
4. The AKS kubelet identity has `AcrPull` on the private ACR.
5. A workspace-based Application Insights component exists.
6. The Blob container `operations` exists, or the workload identity can create it.

The deploy script creates the federated identity credential unless
`-SkipFederatedCredential` is supplied. It also creates an in-cluster Secret for the
Application Insights connection string from the current process environment; the Secret
contains no Azure service credential. The script does not create Azure RBAC assignments.

## Configuration

Environment variables use standard .NET double-underscore nesting.

| Setting | Default | Purpose |
|---|---:|---|
| `Storage__AccountUrl` | required | `https://<account>.blob.core.windows.net` |
| `Storage__ContainerName` | `operations` | Blob container |
| `FaultInjection__LatencyEnabled` | `false` | Enable artificial delay |
| `FaultInjection__LatencyMinMs` | `0` | Minimum delay |
| `FaultInjection__LatencyMaxMs` | `0` | Maximum delay, hard-capped at 30,000 ms |
| `FaultInjection__HttpErrorRate` | `0` | Probability from 0 to 1 |
| `FaultInjection__HttpErrorStatusCode` | `503` | Injected status, restricted to 500-599 |
| `FaultInjection__StorageErrorRate` | `0` | Probability of an injected storage failure |
| `APP_FAULT_LATENCY_MS` | `0` | Chaos-agent alias that enables a fixed delay from 1 to 30,000 ms |
| `APP_FAULT_ERROR_RATE_PERCENT` | `0` | Chaos-agent alias for an HTTP error rate from 1 to 100 percent |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | required in AKS | Application Insights export |

Fault values are in `manifests/configmap.yaml`. Change the ConfigMap and restart the
Deployment to activate a scenario. Never expose a fault-control endpoint.

## Local development

Authenticate without storing credentials:

```powershell
az login
$env:Storage__AccountUrl = 'https://<storage-account>.blob.core.windows.net'
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = '<connection-string>'
dotnet run --project .\src\SreDemo.Api\SreDemo.Api.csproj
```

Exercise the API:

```powershell
Invoke-RestMethod http://localhost:8080/healthz
$created = Invoke-RestMethod -Method Post `
  -Uri http://localhost:8080/api/operations `
  -ContentType application/json `
  -Body '{"message":"private storage dependency check"}'
Invoke-RestMethod "http://localhost:8080/api/operations/$($created.id)"
```

## Test and validate

```powershell
.\scripts\validate.ps1 -SkipDocker
```

Remove `-SkipDocker` to also build the production image. The installed SDK must include
the .NET 8 targeting pack. The build/push script also runs the validation script, checks the image's runtime user,
waits for its Docker health check, and runs Trivy when available.

## Build and push to private ACR

Run from the connected host:

```powershell
$image = .\scripts\build-push.ps1 `
  -AcrName '<private-acr-name>' `
  -ImageTag '20260904.1'
```

The script uses the caller's Azure CLI and Docker login contexts. It does not accept or
persist registry credentials.

## Deploy to private AKS

Set the Application Insights connection string only in the current process, then deploy:

```powershell
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = '<connection-string>'
.\scripts\deploy.ps1 `
  -Action Deploy `
  -AksResourceGroup '<aks-resource-group>' `
  -AksName '<private-aks-name>' `
  -AcrName '<private-acr-name>' `
  -ImageTag '20260904.1' `
  -StorageAccountName '<private-storage-account>' `
  -ManagedIdentityResourceGroup '<identity-resource-group>' `
  -ManagedIdentityName '<workload-identity-name>'
Remove-Item Env:\APPLICATIONINSIGHTS_CONNECTION_STRING
```

The script obtains private-cluster credentials, creates the Entra federated credential
idempotently, creates the telemetry Secret without a temporary file, substitutes
manifest placeholders in memory, applies the manifests, and waits for the rollout.

Access remains internal:

```powershell
kubectl port-forward -n sre-demo service/sre-demo-api 8080:80
Invoke-RestMethod http://localhost:8080/healthz
Invoke-RestMethod http://localhost:8080/ready
```

## Fault demonstrations

Latency example:

```powershell
kubectl patch configmap sre-demo-api-config -n sre-demo --type merge `
  -p '{"data":{"FaultInjection__LatencyEnabled":"true","FaultInjection__LatencyMinMs":"500","FaultInjection__LatencyMaxMs":"1500"}}'
kubectl rollout restart deployment/sre-demo-api -n sre-demo
```

HTTP failures example:

```powershell
kubectl patch configmap sre-demo-api-config -n sre-demo --type merge `
  -p '{"data":{"FaultInjection__HttpErrorRate":"0.25","FaultInjection__HttpErrorStatusCode":"503"}}'
kubectl rollout restart deployment/sre-demo-api -n sre-demo
```

Restore normal behavior:

```powershell
kubectl patch configmap sre-demo-api-config -n sre-demo --type merge `
  -p '{"data":{"FaultInjection__LatencyEnabled":"false","FaultInjection__LatencyMinMs":"0","FaultInjection__LatencyMaxMs":"0","FaultInjection__HttpErrorRate":"0","FaultInjection__HttpErrorStatusCode":"503","FaultInjection__StorageErrorRate":"0"}}'
kubectl rollout restart deployment/sre-demo-api -n sre-demo
```

## Status and rollback

```powershell
.\scripts\deploy.ps1 -Action Status `
  -AksResourceGroup '<aks-resource-group>' -AksName '<private-aks-name>'

.\scripts\deploy.ps1 -Action Rollback `
  -AksResourceGroup '<aks-resource-group>' -AksName '<private-aks-name>'
```

## Telemetry verification

Allow several minutes for ingestion, then query the Application Insights workspace:

```kusto
requests
| where timestamp > ago(30m)
| project timestamp, name, resultCode, duration, operation_Id
| order by timestamp desc
```

```kusto
dependencies
| where timestamp > ago(30m)
| where target has ".blob.core.windows.net"
| project timestamp, name, target, success, resultCode, duration, operation_Id
```

```kusto
traces
| where timestamp > ago(30m)
| where message has_any ("Injected", "Stored operation", "readiness")
| project timestamp, severityLevel, message, operation_Id, customDimensions
```

```kusto
exceptions
| where timestamp > ago(30m)
| project timestamp, type, outerMessage, operation_Id
```

No storage keys, registry passwords, client secrets, or Azure credentials are embedded
in this project.
