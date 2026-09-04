[CmdletBinding()]
param(
    [switch]$SkipDocker
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$Action)

    if ($LASTEXITCODE -ne 0) {
        throw "$Action failed with exit code $LASTEXITCODE."
    }
}

Push-Location $projectRoot
try {
    $installedRuntimes = (& dotnet --list-runtimes) -join "`n"
    $previousRollForward = $env:DOTNET_ROLL_FORWARD
    if ($installedRuntimes -notmatch 'Microsoft\.AspNetCore\.App 8\.') {
        Write-Warning 'ASP.NET Core 8 runtime is unavailable; tests will use major-version runtime roll-forward.'
        $env:DOTNET_ROLL_FORWARD = 'Major'
    }

    & dotnet test .\SreDemo.sln --configuration Release
    Assert-LastExitCode '.NET tests'

    foreach ($script in Get-ChildItem .\scripts\*.ps1) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $script.FullName,
            [ref]$tokens,
            [ref]$errors)
        if ($errors.Count -gt 0) {
            throw "PowerShell syntax validation failed for '$($script.Name)': $($errors -join '; ')"
        }
    }

    if (Get-Command python -ErrorAction SilentlyContinue) {
        & python -c "import pathlib,yaml; files=list(pathlib.Path('manifests').glob('*.yaml')); docs=[yaml.safe_load(p.read_text(encoding='utf-8')) for p in files]; assert all(isinstance(d,dict) and d.get('apiVersion') and d.get('kind') and d.get('metadata',{}).get('name') for d in docs); print(f'Validated {len(docs)} Kubernetes manifests')"
        Assert-LastExitCode 'Kubernetes YAML validation'
    }
    elseif (Get-Command kubectl -ErrorAction SilentlyContinue) {
        & kubectl create --dry-run=client --validate=false -f .\manifests | Out-Null
        Assert-LastExitCode 'Kubernetes manifest client-side validation'
    }
    else {
        Write-Warning 'kubectl is unavailable; Kubernetes manifest validation was skipped.'
    }

    if (-not $SkipDocker) {
        & docker build --tag sre-demo-api:validation .
        Assert-LastExitCode 'Container build'
    }
}
finally {
    $env:DOTNET_ROLL_FORWARD = $previousRollForward
    Pop-Location
}
