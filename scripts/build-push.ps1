[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-zA-Z0-9]+$')]
    [string]$AcrName,

    [ValidatePattern('^[a-z0-9][a-z0-9._-]{0,127}$')]
    [string]$ImageTag = (Get-Date -AsUTC -Format 'yyyyMMddHHmmss'),

    [ValidatePattern('^[a-z0-9]+(?:[._-][a-z0-9]+)*$')]
    [string]$ImageName = 'sre-demo-api',

    [switch]$SkipTests,

    [switch]$SkipSmokeTest,

    [switch]$SkipScan
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$Action)

    if ($LASTEXITCODE -ne 0) {
        throw "$Action failed with exit code $LASTEXITCODE."
    }
}

Assert-Command az
Assert-Command docker

Push-Location $projectRoot
try {
    & az account show --only-show-errors --output none
    Assert-LastExitCode 'Azure CLI authentication check'

    & docker info --format '{{.ServerVersion}}' | Out-Null
    Assert-LastExitCode 'Docker engine check'

    if (-not $SkipTests) {
        Assert-Command dotnet
        & .\scripts\validate.ps1 -SkipDocker
    }

    $loginServer = ((& az acr show `
        --name $AcrName `
        --query loginServer `
        --output tsv `
        --only-show-errors) -join "`n").Trim()
    Assert-LastExitCode 'ACR lookup'

    if ([string]::IsNullOrWhiteSpace($loginServer)) {
        throw "Could not resolve the login server for ACR '$AcrName'."
    }

    & az acr login --name $AcrName --only-show-errors
    Assert-LastExitCode 'ACR login'

    $image = "$loginServer/$ImageName`:$ImageTag"
    & docker build --pull --tag $image .
    Assert-LastExitCode 'Container build'

    $containerUser = (& docker image inspect $image --format '{{.Config.User}}').Trim()
    Assert-LastExitCode 'Container user inspection'
    if ([string]::IsNullOrWhiteSpace($containerUser) -or $containerUser -in @('0', 'root')) {
        throw "The built image does not declare a non-root runtime user."
    }

    if (-not $SkipSmokeTest) {
        $containerName = "sre-demo-api-smoke-$([Guid]::NewGuid().ToString('n'))"
        $containerId = $null
        try {
            $containerId = (& docker run `
                --detach `
                --rm `
                --name $containerName `
                $image).Trim()
            Assert-LastExitCode 'Container smoke-test start'

            $deadline = (Get-Date).AddSeconds(75)
            do {
                Start-Sleep -Seconds 2
                $health = (& docker inspect $containerId --format '{{.State.Health.Status}}').Trim()
                Assert-LastExitCode 'Container health inspection'
                if ($health -eq 'healthy') {
                    break
                }
                if ($health -eq 'unhealthy') {
                    & docker logs $containerId
                    throw 'Container smoke test became unhealthy.'
                }
            } while ((Get-Date) -lt $deadline)

            if ($health -ne 'healthy') {
                & docker logs $containerId
                throw 'Container smoke test did not become healthy before the timeout.'
            }
        }
        finally {
            if ($containerId) {
                & docker rm --force $containerId | Out-Null
            }
        }
    }

    if (-not $SkipScan) {
        if (Get-Command trivy -ErrorAction SilentlyContinue) {
            & trivy image --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed $image
            Assert-LastExitCode 'Trivy image scan'
        }
        else {
            Write-Warning 'Trivy is not installed; no local vulnerability scanner was available.'
        }
    }

    & docker push $image
    Assert-LastExitCode 'Container push'

    Write-Output $image
}
finally {
    Pop-Location
}
