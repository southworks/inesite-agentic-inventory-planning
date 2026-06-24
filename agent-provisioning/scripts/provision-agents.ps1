param(
    [string]$ConfigPath = "agent-provisioning/config/provisioning.local.json",
    [string]$AgentsPath = "agent-provisioning/agents"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

Push-Location $repoRoot
try {
    dotnet run --project agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning -- `
        --config $ConfigPath `
        --agents $AgentsPath
}
finally {
    Pop-Location
}
