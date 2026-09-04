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

    [string]$ImageTag,

    [string]$StorageAccountName,

    [string]$ManagedIdentityResourceGroup,

    [string]$ManagedIdentityName,

    [string]$FederatedCredentialName = 'sre-demo-api',

    [switch]$SkipFederatedCredential
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestRoot = Join-Path $projectRoot 'manifests'
$namespace = 'sre-demo'
$serviceAccount = 'sre-demo-api'
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

function Invoke-AzTsv {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $result = (& az @Arguments --only-show-errors --output tsv)
    Assert-LastExitCode 'Azure CLI command'
    return ($result -join "`n").Trim()
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

$requiredValues = @{
    AcrName = $AcrName
    ImageTag = $ImageTag
    StorageAccountName = $StorageAccountName
    ManagedIdentityResourceGroup = $ManagedIdentityResourceGroup
    ManagedIdentityName = $ManagedIdentityName
}
foreach ($entry in $requiredValues.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.Value)) {
        throw "-$($entry.Key) is required for Deploy."
    }
}

$applicationInsightsConnectionString = $env:APPLICATIONINSIGHTS_CONNECTION_STRING
if ([string]::IsNullOrWhiteSpace($applicationInsightsConnectionString)) {
    throw 'Set APPLICATIONINSIGHTS_CONNECTION_STRING in the current process before deploying.'
}
if ($applicationInsightsConnectionString.Contains('"') -or
    $applicationInsightsConnectionString.Contains("`r") -or
    $applicationInsightsConnectionString.Contains("`n")) {
    throw 'APPLICATIONINSIGHTS_CONNECTION_STRING contains unsupported YAML characters.'
}

$clientId = Invoke-AzTsv -Arguments @(
    'identity', 'show',
    '--resource-group', $ManagedIdentityResourceGroup,
    '--name', $ManagedIdentityName,
    '--query', 'clientId'
)
if ([string]::IsNullOrWhiteSpace($clientId)) {
    throw "Could not resolve the client ID for managed identity '$ManagedIdentityName'."
}

if (-not $SkipFederatedCredential) {
    $issuer = Invoke-AzTsv -Arguments @(
        'aks', 'show',
        '--resource-group', $AksResourceGroup,
        '--name', $AksName,
        '--query', 'oidcIssuerProfile.issuerUrl'
    )
    if ([string]::IsNullOrWhiteSpace($issuer)) {
        throw 'AKS does not expose an OIDC issuer. Enable OIDC issuer and Workload Identity on the cluster.'
    }

    $federatedSubject = "system:serviceaccount:$namespace`:$serviceAccount"
    $existingCredential = Invoke-AzTsv -Arguments @(
        'identity', 'federated-credential', 'list',
        '--resource-group', $ManagedIdentityResourceGroup,
        '--identity-name', $ManagedIdentityName,
        '--query', "[?issuer=='$issuer' && subject=='$federatedSubject'].name | [0]"
    )
    if ([string]::IsNullOrWhiteSpace($existingCredential)) {
        & az identity federated-credential create `
            --resource-group $ManagedIdentityResourceGroup `
            --identity-name $ManagedIdentityName `
            --name $FederatedCredentialName `
            --issuer $issuer `
            --subject $federatedSubject `
            --audiences 'api://AzureADTokenExchange' `
            --only-show-errors `
            --output none
        Assert-LastExitCode 'Federated identity credential creation'
    }
}

$loginServer = Invoke-AzTsv -Arguments @(
    'acr', 'show',
    '--name', $AcrName,
    '--query', 'loginServer'
)
$replacements = [ordered]@{
    '__AZURE_CLIENT_ID__' = $clientId
    '__STORAGE_ACCOUNT_URL__' = "https://$StorageAccountName.blob.core.windows.net"
    '__IMAGE__' = "$loginServer/$ImageName`:$ImageTag"
}

$manifestFiles = @(
    'namespace.yaml',
    'serviceaccount.yaml',
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

    if ($manifestFile -eq 'namespace.yaml') {
        $encodedConnectionString = [Convert]::ToBase64String(
            [Text.Encoding]::UTF8.GetBytes($applicationInsightsConnectionString))
        $telemetrySecret = @{
            apiVersion = 'v1'
            kind = 'Secret'
            metadata = @{
                name = 'sre-demo-api-telemetry'
                namespace = $namespace
                labels = @{
                    'app.kubernetes.io/name' = 'sre-demo-api'
                }
            }
            type = 'Opaque'
            data = @{
                'connection-string' = $encodedConnectionString
            }
        } | ConvertTo-Json -Depth 8

        $telemetrySecret | & kubectl apply -f -
        Assert-LastExitCode 'Applying runtime telemetry Secret'
    }
}

& kubectl rollout status "deployment/$deployment" --namespace $namespace --timeout 300s
Assert-LastExitCode 'Deployment rollout'

& kubectl get pods,service,pdb `
    --namespace $namespace `
    --selector 'app.kubernetes.io/name=sre-demo-api' `
    --output wide
Assert-LastExitCode 'Post-deployment status'
