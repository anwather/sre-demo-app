#!/usr/bin/env bash
set -euo pipefail

skip_docker=false

usage() {
  cat <<'EOF'
Usage: ./scripts/validate.sh [--skip-docker]

Validates the .NET application, PowerShell and Bash scripts, Kubernetes
manifests, and optionally the production container image.
EOF
}

while (($# > 0)); do
  case "$1" in
    --skip-docker)
      skip_docker=true
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

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/.." && pwd)"
cd "$project_root"

if command -v dotnet >/dev/null 2>&1; then
  dotnet test ./SreDemo.sln --configuration Release
else
  require_command docker
  docker info >/dev/null
  printf 'dotnet is not installed; running tests in the .NET 8 SDK container.\n'
  docker build --target test --tag sre-demo-api:test-validation .
fi

for script in ./scripts/*.sh; do
  bash -n "$script"
done

if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoLogo -NoProfile -Command '
    $ErrorActionPreference = "Stop"
    foreach ($script in Get-ChildItem ./scripts/*.ps1) {
      $tokens = $null
      $errors = $null
      [void][System.Management.Automation.Language.Parser]::ParseFile(
        $script.FullName,
        [ref]$tokens,
        [ref]$errors)
      if ($errors.Count -gt 0) {
        throw "PowerShell syntax validation failed for $($script.Name): $($errors -join "; ")"
      }
    }
  '
fi

if command -v python3 >/dev/null 2>&1 &&
   python3 -c 'import yaml' >/dev/null 2>&1; then
  python3 - <<'PY'
from pathlib import Path
import yaml

files = list(Path("manifests").glob("*.yaml"))
documents = [yaml.safe_load(path.read_text(encoding="utf-8")) for path in files]
assert all(
    isinstance(document, dict)
    and document.get("apiVersion")
    and document.get("kind")
    and document.get("metadata", {}).get("name")
    for document in documents
)
print(f"Validated {len(documents)} Kubernetes manifests")
PY
elif command -v kubectl >/dev/null 2>&1; then
  kubectl create --dry-run=client --validate=false -f ./manifests >/dev/null
else
  printf 'Warning: Kubernetes manifest validation was skipped; install kubectl or python3 with PyYAML.\n' >&2
fi

if [[ "$skip_docker" == false ]]; then
  require_command docker
  docker info >/dev/null
  docker build --tag sre-demo-api:validation .
fi
