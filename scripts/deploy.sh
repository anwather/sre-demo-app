#!/usr/bin/env bash
set -euo pipefail

action="deploy"
aks_resource_group=""
aks_name=""
acr_name=""
image_name="sre-demo-api"
image_tag=""
namespace="sre-demo"
deployment="sre-demo-api"

usage() {
  cat <<'EOF'
Usage: ./scripts/deploy.sh --aks-resource-group RG --aks-name NAME [options]

Required:
  --aks-resource-group RG  AKS resource group
  --aks-name NAME          AKS cluster name

Deploy options:
  --acr-name NAME          Azure Container Registry name
  --image-name NAME        Image repository name (default: sre-demo-api)
  --image-tag TAG          Image tag to deploy

General:
  --action ACTION          deploy, status, or rollback (default: deploy)
  -h, --help               Show this help
EOF
}

while (($# > 0)); do
  case "$1" in
    --action)
      action="${2:?Missing value for --action}"
      shift 2
      ;;
    --aks-resource-group)
      aks_resource_group="${2:?Missing value for --aks-resource-group}"
      shift 2
      ;;
    --aks-name)
      aks_name="${2:?Missing value for --aks-name}"
      shift 2
      ;;
    --acr-name)
      acr_name="${2:?Missing value for --acr-name}"
      shift 2
      ;;
    --image-name)
      image_name="${2:?Missing value for --image-name}"
      shift 2
      ;;
    --image-tag)
      image_tag="${2:?Missing value for --image-tag}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown argument: %s\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    printf "Required command '%s' was not found.\n" "$1" >&2
    exit 1
  fi
}

case "$action" in
  deploy|status|rollback) ;;
  *)
    printf '%s\n' '--action must be deploy, status, or rollback.' >&2
    exit 2
    ;;
esac

if [[ -z "$aks_resource_group" || -z "$aks_name" ]]; then
  printf '%s\n' '--aks-resource-group and --aks-name are required.' >&2
  usage >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"
manifest_root="$project_root/manifests"

require_command az
require_command kubectl

az account show --only-show-errors --output none
az aks get-credentials \
  --resource-group "$aks_resource_group" \
  --name "$aks_name" \
  --overwrite-existing \
  --only-show-errors
kubectl cluster-info >/dev/null

if [[ "$action" == "status" ]]; then
  kubectl get deployment,pods,service,pdb \
    --namespace "$namespace" \
    --selector 'app.kubernetes.io/name=sre-demo-api' \
    --output wide
  exit 0
fi

if [[ "$action" == "rollback" ]]; then
  kubectl rollout undo "deployment/$deployment" --namespace "$namespace"
  kubectl rollout status "deployment/$deployment" \
    --namespace "$namespace" \
    --timeout 180s
  exit 0
fi

if [[ -z "$acr_name" || -z "$image_tag" ]]; then
  printf '%s\n' '--acr-name and --image-tag are required for deploy.' >&2
  exit 2
fi

login_server="$(
  az acr show \
    --name "$acr_name" \
    --query loginServer \
    --output tsv \
    --only-show-errors
)"

if [[ -z "$login_server" ]]; then
  printf "Could not resolve the login server for ACR '%s'.\n" "$acr_name" >&2
  exit 1
fi

image="${login_server}/${image_name}:${image_tag}"
manifest_files=(
  namespace.yaml
  configmap.yaml
  deployment.yaml
  service.yaml
  pdb.yaml
)

for manifest_file in "${manifest_files[@]}"; do
  rendered="$(sed "s|__IMAGE__|${image}|g" "$manifest_root/$manifest_file")"
  if grep -Eq '__[A-Z0-9_]+__' <<<"$rendered"; then
    printf "Manifest '%s' contains an unresolved placeholder.\n" "$manifest_file" >&2
    exit 1
  fi
  printf '%s\n' "$rendered" | kubectl apply -f -
done

kubectl rollout status "deployment/$deployment" \
  --namespace "$namespace" \
  --timeout 300s

kubectl get pods,service,pdb \
  --namespace "$namespace" \
  --selector 'app.kubernetes.io/name=sre-demo-api' \
  --output wide
