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

Run from a Linux host that can reach the private AKS API and private ACR endpoint:

- .NET 8 SDK
- Docker
- Azure CLI
- `kubectl`
- Optional: Trivy

Authenticate and select the deployment subscription:

```bash
az login --use-device-code
az account set --subscription 5587ff14-5a95-41f2-8ce4-d2f702958783
```

Clone the repository:

```bash
git clone https://github.com/anwather/sre-demo-app.git
cd sre-demo-app
```

## Validate

```bash
./scripts/validate.sh --skip-docker
```

Remove `--skip-docker` to include a production container build.

## Build and push the image to ACR

```bash
./scripts/build-push.sh \
  --acr-name acrsredemoanw260904 \
  --image-tag 20260904.1
```

The script builds the image locally on the VM, validates that it runs as a non-root
container, performs a health check, and pushes the resulting image to the private ACR.

## Apply the manifests to AKS

```bash
./scripts/deploy.sh \
  --aks-resource-group rg-sredemo-aks-aue \
  --aks-name aks-sredemo-aue \
  --acr-name acrsredemoanw260904 \
  --image-tag 20260904.1
```

## Access

The Service is internal:

```bash
kubectl port-forward -n sre-demo service/sre-demo-api 8080:80
curl http://localhost:8080/
curl http://localhost:8080/api/products
```

Create an order:

```bash
curl \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"productId":1,"quantity":2}' \
  http://localhost:8080/api/orders
```

## Status and rollback

```bash
./scripts/deploy.sh \
  --action status \
  --aks-resource-group rg-sredemo-aks-aue \
  --aks-name aks-sredemo-aue

./scripts/deploy.sh \
  --action rollback \
  --aks-resource-group rg-sredemo-aks-aue \
  --aks-name aks-sredemo-aue
```

PowerShell equivalents remain available in `scripts/*.ps1`.
