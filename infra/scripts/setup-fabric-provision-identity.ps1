param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    [string]$IdentityName = 'invplan-fabric-provision-identity',
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceName,
    [string]$LakehouseName = 'InventoryPlanningLakehouse',
    [string]$Location = 'eastus',
    [ValidateSet('Admin', 'Contributor', 'Member', 'Viewer')]
    [string]$FabricRole = 'Contributor',
    [switch]$SkipWorkspaceRoleAssignment,
    [int]$RoleAssignmentRetryCount = 15,
    [int]$RoleAssignmentRetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) { $ResourceGroupName = $env:FABRIC_RESOURCE_GROUP_NAME }
if ([string]::IsNullOrWhiteSpace($IdentityName)) { $IdentityName = $env:FABRIC_IDENTITY_NAME }
if ([string]::IsNullOrWhiteSpace($WorkspaceName)) { $WorkspaceName = $env:FABRIC_WORKSPACE_NAME }
if ([string]::IsNullOrWhiteSpace($LakehouseName)) { $LakehouseName = $env:FABRIC_LAKEHOUSE_NAME }
if ([string]::IsNullOrWhiteSpace($Location)) { $Location = $env:FABRIC_LOCATION }
if ([string]::IsNullOrWhiteSpace($FabricRole)) { $FabricRole = if ($env:FABRIC_ROLE) { $env:FABRIC_ROLE } else { 'Contributor' } }

function Get-AzureAccessToken {
    try {
        $account = az account show -o json 2>$null | ConvertFrom-Json
        if ($account) { return }
    }
    catch {}

    try {
        $null = Get-AzAccessToken -ErrorAction Stop
        return
    }
    catch {}

    throw 'Not logged in to Azure. Run az login or Connect-AzAccount first.'
}

function Get-FabricAccessToken {
    $resource = 'https://api.fabric.microsoft.com'
    try {
        return (Get-AzAccessToken -ResourceUrl $resource).Token
    }
    catch {
        $prevEAP = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $token = az account get-access-token --resource $resource --query accessToken -o tsv 2>$null
        $ErrorActionPreference = $prevEAP
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
            throw "Unable to acquire Fabric access token for '$resource'."
        }
        return $token
    }
}

Get-AzureAccessToken

Write-Host '=== Setup Fabric Provision Identity (Inventory Planning) ==='
Write-Host "ResourceGroupName: $ResourceGroupName"
Write-Host "IdentityName: $IdentityName"
Write-Host "WorkspaceName: $WorkspaceName"
Write-Host "Location: $Location"
Write-Host "FabricRole: $FabricRole"

$WorkspaceId = ''
if (-not $SkipWorkspaceRoleAssignment) {
    Write-Host "Resolving workspace ID for '$WorkspaceName'..."
    $fabricToken = Get-FabricAccessToken
    $workspaces = Invoke-RestMethod -Method GET -Uri 'https://api.fabric.microsoft.com/v1/workspaces' -Headers @{ Authorization = "Bearer $fabricToken" }
    $match = @($workspaces.value) | Where-Object { $_.displayName -eq $WorkspaceName } | Select-Object -First 1
    if (-not $match) {
        throw "Workspace '$WorkspaceName' was not found in Fabric."
    }
    $WorkspaceId = $match.id
    Write-Host "Resolved WorkspaceId: $WorkspaceId"
}

$prevEAP = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$rg = az group show --name $ResourceGroupName -o json 2>$null | ConvertFrom-Json
$ErrorActionPreference = $prevEAP
if (-not $rg) {
    Write-Host "Creating resource group '$ResourceGroupName' in '$Location'..."
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    az group create --name $ResourceGroupName --location $Location -o none
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create resource group '$ResourceGroupName'."
    }
}
else {
    Write-Host "Resource group '$ResourceGroupName' already exists."
}

$prevEAP = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$identity = az identity show --resource-group $ResourceGroupName --name $IdentityName -o json 2>$null | ConvertFrom-Json
$ErrorActionPreference = $prevEAP
if ($identity) {
    Write-Host "Managed identity '$IdentityName' already exists."
}
else {
    Write-Host "Creating managed identity '$IdentityName'..."
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $identity = az identity create --resource-group $ResourceGroupName --name $IdentityName --location $Location -o json 2>$null | ConvertFrom-Json
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -ne 0 -or -not $identity) {
        throw "Failed to create managed identity '$IdentityName'."
    }
    Write-Host "Managed identity '$IdentityName' created."
}

$identityId = [string]$identity.id
$clientId = [string]$identity.clientId
$principalId = [string]$identity.principalId

Write-Host "Identity resource ID: $identityId"
Write-Host "Identity client ID: $clientId"
Write-Host "Identity principal ID: $principalId"

if (-not $SkipWorkspaceRoleAssignment) {
    Write-Host "Assigning '$FabricRole' role on workspace '$WorkspaceId' to identity '$IdentityName'..."

    $fabricToken = Get-FabricAccessToken
    $body = @{
        principal = @{
            id                      = $principalId
            type                    = 'ServicePrincipal'
            servicePrincipalDetails = @{
                aadAppId = $clientId
            }
        }
        role = $FabricRole
    } | ConvertTo-Json -Depth 5

    $roleAssigned = $false
    for ($attempt = 1; $attempt -le $RoleAssignmentRetryCount; $attempt++) {
        try {
            if ($attempt -gt 1) {
                Write-Host "Retrying role assignment (attempt $attempt/$RoleAssignmentRetryCount)..."
                $fabricToken = Get-FabricAccessToken
            }

            $response = Invoke-RestMethod `
                -Method POST `
                -Uri "https://api.fabric.microsoft.com/v1/workspaces/$WorkspaceId/roleAssignments" `
                -Headers @{ Authorization = "Bearer $fabricToken"; 'Content-Type' = 'application/json' } `
                -Body $body `
                -ErrorAction Stop
            Write-Host "Fabric workspace role '$FabricRole' assigned successfully."
            $roleAssigned = $true
            break
        }
        catch {
            $errorMessage = $_.Exception.Message
            if ($_.Exception -is [System.Net.WebException] -and $_.Exception.Response) {
                try {
                    $stream = $_.Exception.Response.GetResponseStream()
                    $reader = New-Object System.IO.StreamReader($stream)
                    $responseBody = $reader.ReadToEnd()
                    $errorDetail = $responseBody | ConvertFrom-Json
                    $errorMessage = "$($errorDetail.errorCode): $($errorDetail.message)"
                }
                catch {}
            }
            elseif ($_.ErrorDetails.Message) {
                try {
                    $errorDetail = $_.ErrorDetails.Message | ConvertFrom-Json
                    $errorMessage = "$($errorDetail.errorCode): $($errorDetail.message)"
                }
                catch {}
            }

            if ($errorMessage -match '409|already.*assign|Conflict') {
                Write-Host "Identity already has a role assignment on workspace '$WorkspaceId'. Continuing."
                $roleAssigned = $true
                break
            }

            if ($errorMessage -match 'PrincipalNotFound' -and $attempt -lt $RoleAssignmentRetryCount) {
                Write-Host "Principal not yet propagated to Entra ID (attempt $attempt/$RoleAssignmentRetryCount). Retrying in ${RoleAssignmentRetryDelaySeconds}s..."
                Start-Sleep -Seconds $RoleAssignmentRetryDelaySeconds
                continue
            }

            throw "Failed to assign Fabric workspace role: $errorMessage"
        }
    }

    if (-not $roleAssigned) {
        throw "Failed to assign Fabric workspace role after $RoleAssignmentRetryCount attempts."
    }
}
else {
    Write-Host 'Skipping Fabric workspace role assignment (SkipWorkspaceRoleAssignment).'
}

Write-Host ''
Write-Host 'Setup completed successfully.'
