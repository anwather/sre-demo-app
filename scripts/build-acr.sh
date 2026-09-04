#!/usr/bin/env bash
set -euo pipefail

acr_name=""
image_name="sre-demo-api"
image_tag="$(date -u +%Y%m%d%H%M%S)"
agent_pool=""
no_wait=false

usage() {
  cat <<'EOF'
Usage: ./scripts/build-acr.sh --acr-name NAME [options]

Submits the current repository to Azure Container Registry Tasks. No local
Docker engine or .NET SDK is required.

Required:
  --acr-name NAME          Azure Container Registry name

Options:
  --image-name NAME        Image repository name (default: sre-demo-api)
  --image-tag TAG          Image tag (default: current UTC timestamp)
  --agent-pool NAME        Dedicated ACR agent pool for a private registry
  --no-wait                Queue the build without streaming logs
  -h, --help               Show this help
EOF
}

while (($# > 0)); do
  case "$1" in
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
    --agent-pool)
      agent_pool="${2:?Missing value for --agent-pool}"
      shift 2
      ;;
    --no-wait)
      no_wait=true
      shift
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

if [[ -z "$acr_name" ]]; then
  printf '%s\n' '--acr-name is required.' >&2
  usage >&2
  exit 2
fi

if [[ ! "$acr_name" =~ ^[A-Za-z0-9]+$ ]]; then
  printf '%s\n' '--acr-name must contain only letters and numbers.' >&2
  exit 2
fi

if [[ ! "$image_name" =~ ^[a-z0-9]+([._-][a-z0-9]+)*$ ]]; then
  printf '%s\n' '--image-name is invalid.' >&2
  exit 2
fi

if [[ ! "$image_tag" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]]; then
  printf '%s\n' '--image-tag is invalid.' >&2
  exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"

require_command az
az account show --only-show-errors --output none

readarray -t registry_settings < <(
  az acr show \
    --name "$acr_name" \
    --query '[loginServer,publicNetworkAccess,networkRuleBypassOptions]' \
    --output tsv \
    --only-show-errors |
    tr -d '\r'
)

login_server="${registry_settings[0]:-}"
public_network_access="${registry_settings[1]:-}"
network_rule_bypass="${registry_settings[2]:-}"

if [[ -z "$login_server" ]]; then
  printf "Could not resolve ACR '%s'.\n" "$acr_name" >&2
  exit 1
fi

if [[ -z "$agent_pool" && "$public_network_access" == "Disabled" ]]; then
  cat >&2 <<EOF
ACR '$acr_name' has public network access disabled and cannot use the shared
ACR Tasks worker pool. Supply --agent-pool with a dedicated ACR agent pool
that has network access to the registry's private endpoint.

Current network bypass setting: ${network_rule_bypass:-unknown}
EOF
  exit 1
fi

if [[ -n "$agent_pool" ]]; then
  pool_state="$(
    az acr agentpool show \
      --registry "$acr_name" \
      --name "$agent_pool" \
      --query provisioningState \
      --output tsv \
      --only-show-errors
  )"

  if [[ "$pool_state" != "Succeeded" ]]; then
    printf "ACR agent pool '%s' is not ready; provisioning state is '%s'.\n" \
      "$agent_pool" "${pool_state:-unknown}" >&2
    exit 1
  fi
fi

build_arguments=(
  acr build
  --registry "$acr_name"
  --image "${image_name}:${image_tag}"
  --file Dockerfile
  --platform linux/amd64
)

if [[ -n "$agent_pool" ]]; then
  build_arguments+=(--agent-pool "$agent_pool")
fi

if [[ "$no_wait" == true ]]; then
  build_arguments+=(--no-wait)
fi

az "${build_arguments[@]}" "$project_root"

image="${login_server}/${image_name}:${image_tag}"
if [[ "$no_wait" == true ]]; then
  printf 'Queued ACR build for %s\n' "$image"
else
  printf 'Published %s\n' "$image"
fi
