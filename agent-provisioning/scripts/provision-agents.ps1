$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

Push-Location $repoRoot
try {
    dotnet run --project agent-provisioning/src/CohereInventoryAndTrend.AgentProvisioning
}
finally {
    Pop-Location
}
