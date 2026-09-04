#!/usr/bin/env bash
set -euo pipefail

acr_name=""
image_name="sre-demo-api"
image_tag="$(date -u +%Y%m%d%H%M%S)"
skip_tests=false
skip_smoke_test=false
skip_scan=false

usage() {
  cat <<'EOF'
Usage: ./scripts/build-push.sh --acr-name NAME [options]

Required:
  --acr-name NAME          Azure Container Registry name

Options:
  --image-name NAME        Image repository name (default: sre-demo-api)
  --image-tag TAG          Image tag (default: current UTC timestamp)
  --skip-tests             Skip application and manifest validation
  --skip-smoke-test        Skip the container health check
  --skip-scan              Skip Trivy even when it is installed
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
    --skip-tests)
      skip_tests=true
      shift
      ;;
    --skip-smoke-test)
      skip_smoke_test=true
      shift
      ;;
    --skip-scan)
      skip_scan=true
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
cd "$project_root"

require_command az
require_command docker

az account show --only-show-errors --output none
docker info >/dev/null

if [[ "$skip_tests" == false ]]; then
  "$script_dir/validate.sh" --skip-docker
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

az acr login --name "$acr_name" --only-show-errors

image="${login_server}/${image_name}:${image_tag}"
docker build --pull --tag "$image" .

container_user="$(docker image inspect "$image" --format '{{.Config.User}}')"
if [[ -z "$container_user" || "$container_user" == "0" || "$container_user" == "root" ]]; then
  printf 'The built image does not declare a non-root runtime user.\n' >&2
  exit 1
fi

if [[ "$skip_smoke_test" == false ]]; then
  container_name="sre-demo-api-smoke-$(date +%s)-$$"
  container_id=""

  cleanup() {
    if [[ -n "$container_id" ]]; then
      docker rm --force "$container_id" >/dev/null 2>&1 || true
    fi
  }
  trap cleanup EXIT

  container_id="$(docker run --detach --rm --name "$container_name" "$image")"
  deadline=$((SECONDS + 75))
  health=""

  while ((SECONDS < deadline)); do
    sleep 2
    health="$(docker inspect "$container_id" --format '{{.State.Health.Status}}')"
    if [[ "$health" == "healthy" ]]; then
      break
    fi
    if [[ "$health" == "unhealthy" ]]; then
      docker logs "$container_id"
      printf 'Container smoke test became unhealthy.\n' >&2
      exit 1
    fi
  done

  if [[ "$health" != "healthy" ]]; then
    docker logs "$container_id"
    printf 'Container smoke test did not become healthy before the timeout.\n' >&2
    exit 1
  fi

  cleanup
  container_id=""
  trap - EXIT
fi

if [[ "$skip_scan" == false ]]; then
  if command -v trivy >/dev/null 2>&1; then
    trivy image --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed "$image"
  else
    printf 'Warning: Trivy is not installed; no local vulnerability scanner was available.\n' >&2
  fi
fi

docker push "$image"
printf '%s\n' "$image"
