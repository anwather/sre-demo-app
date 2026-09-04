# Catalog API

A small, self-contained .NET 8 web API for deployment to Azure Kubernetes Service.
The application has no database, storage account, workload identity, external API, or
other managed-service dependency.

## Kubernetes resources

- Namespace
- ConfigMap
- Deployment with two replicas
- Internal `ClusterIP` Service
- Startup, liveness, and readiness probes
- PodDisruptionBudget requiring one available replica
- CPU and memory requests and limits
- Non-root container with a read-only root filesystem and dropped Linux capabilities

The application is stateless, so it does not require a persistent volume. Standard JSON
logs are written to stdout and are collected by the AKS cluster's existing Container
Insights configuration.

## API

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/` | Service information |
| `GET` | `/healthz` | Liveness |
| `GET` | `/ready` | Readiness |
| `GET` | `/api/products` | List the built-in product catalog |
| `GET` | `/api/products/{id}` | Get one product |
| `POST` | `/api/orders` | Validate and return a transient order |

Example order:

```json
{
  "productId": 1,
  "quantity": 2
}
```

## Prerequisites

Run from a host that can reach the private AKS API and private ACR endpoint:

- PowerShell 7
- .NET 8 SDK
- Docker
- Azure CLI
- `kubectl`
- Optional: Trivy

## Validate

```powershell
./scripts/validate.ps1 -SkipDocker
```

Remove `-SkipDocker` to include a production container build.

## Build and push the image to ACR

```powershell
$image = ./scripts/build-push.ps1 `
  -AcrName 'acrsredemoanw260904' `
  -ImageTag '20260904.1'
```

The script builds the image locally on the VM, validates that it runs as a non-root
container, performs a health check, and pushes the resulting image to the private ACR.

## Apply the manifests to AKS

```powershell
./scripts/deploy.ps1 `
  -Action Deploy `
  -AksResourceGroup 'rg-sredemo-aks-aue' `
  -AksName 'aks-sredemo-aue' `
  -AcrName 'acrsredemoanw260904' `
  -ImageTag '20260904.1'
```

## Access

The Service is internal:

```powershell
kubectl port-forward -n sre-demo service/sre-demo-api 8080:80
Invoke-RestMethod http://localhost:8080/
Invoke-RestMethod http://localhost:8080/api/products
```

Create an order:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:8080/api/orders `
  -ContentType application/json `
  -Body '{"productId":1,"quantity":2}'
```

## Status and rollback

```powershell
./scripts/deploy.ps1 -Action Status `
  -AksResourceGroup 'rg-sredemo-aks-aue' `
  -AksName 'aks-sredemo-aue'

./scripts/deploy.ps1 -Action Rollback `
  -AksResourceGroup 'rg-sredemo-aks-aue' `
  -AksName 'aks-sredemo-aue'
```
