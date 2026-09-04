[CmdletBinding()]
param(
    [ValidateSet('Deploy', 'Rollback', 'Status')]
    [string]$Action = 'Deploy',

    [Parameter(Mandatory)]
    [string]$AksResourceGroup,

    [Parameter(Mandatory)]
    [string]$AksName,

    [string]$AcrName,

    [string]$ImageName = 'sre-demo-api',

    [string]$ImageTag
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestRoot = Join-Path $projectRoot 'manifests'
$namespace = 'sre-demo'
$deployment = 'sre-demo-api'

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found."
    }
}

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$ActionName)

    if ($LASTEXITCODE -ne 0) {
        throw "$ActionName failed with exit code $LASTEXITCODE."
    }
}

Assert-Command az
Assert-Command kubectl

& az account show --only-show-errors --output none
Assert-LastExitCode 'Azure CLI authentication check'

& az aks get-credentials `
    --resource-group $AksResourceGroup `
    --name $AksName `
    --overwrite-existing `
    --only-show-errors
Assert-LastExitCode 'AKS kubeconfig update'

& kubectl cluster-info | Out-Null
Assert-LastExitCode 'Private AKS connectivity check'

if ($Action -eq 'Status') {
    & kubectl get deployment,pods,service,pdb `
        --namespace $namespace `
        --selector 'app.kubernetes.io/name=sre-demo-api' `
        --output wide
    Assert-LastExitCode 'Workload status'
    return
}

if ($Action -eq 'Rollback') {
    & kubectl rollout undo "deployment/$deployment" --namespace $namespace
    Assert-LastExitCode 'Deployment rollback'
    & kubectl rollout status "deployment/$deployment" --namespace $namespace --timeout 180s
    Assert-LastExitCode 'Rollback status'
    return
}

if ([string]::IsNullOrWhiteSpace($AcrName)) {
    throw '-AcrName is required for Deploy.'
}
if ([string]::IsNullOrWhiteSpace($ImageTag)) {
    throw '-ImageTag is required for Deploy.'
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

$replacements = [ordered]@{
    '__IMAGE__' = "$loginServer/$ImageName`:$ImageTag"
}

$manifestFiles = @(
    'namespace.yaml',
    'configmap.yaml',
    'deployment.yaml',
    'service.yaml',
    'pdb.yaml'
)

foreach ($manifestFile in $manifestFiles) {
    $path = Join-Path $manifestRoot $manifestFile
    $rendered = Get-Content -Path $path -Raw
    foreach ($replacement in $replacements.GetEnumerator()) {
        $rendered = $rendered.Replace($replacement.Key, $replacement.Value)
    }

    if ($rendered -match '__[A-Z0-9_]+__') {
        throw "Manifest '$manifestFile' contains an unresolved placeholder."
    }

    $rendered | & kubectl apply -f -
    Assert-LastExitCode "Applying $manifestFile"
}

& kubectl rollout status "deployment/$deployment" --namespace $namespace --timeout 300s
Assert-LastExitCode 'Deployment rollout'

& kubectl get pods,service,pdb `
    --namespace $namespace `
    --selector 'app.kubernetes.io/name=sre-demo-api' `
    --output wide
Assert-LastExitCode 'Post-deployment status'
