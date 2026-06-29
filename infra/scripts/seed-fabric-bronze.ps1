param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceId,

    [Parameter(Mandatory = $true)]
    [string]$LakehouseId,

    [Parameter(Mandatory = $true)]
    [string]$CasesPath,

    [string]$OneLakeEndpoint = 'https://onelake.dfs.fabric.microsoft.com'
)

$ErrorActionPreference = 'Stop'

function Get-OneLakeAccessToken {
    $resource = 'https://storage.azure.com'

    if (-not [string]::IsNullOrWhiteSpace($env:AZURE_CLIENT_ID)) {
        try {
            $uri = "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=$resource&client_id=$($env:AZURE_CLIENT_ID)"
            $resp = Invoke-RestMethod -Uri $uri -Headers @{ Metadata = 'true' }
            if (-not [string]::IsNullOrWhiteSpace($resp.access_token)) {
                return $resp.access_token
            }
        }
        catch {
            Write-Verbose "IMDS token request failed: $_"
        }
    }

    try {
        return (Get-AzAccessToken -ResourceUrl $resource).Token
    }
    catch {}

    $token = az account get-access-token --resource $resource --query accessToken -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($token)) {
        return $token
    }

    throw "Unable to acquire access token for '$resource'."
}

Write-Host '=== Fabric bronze seed (Inventory Planning) ==='
Write-Host "WorkspaceId: $WorkspaceId"
Write-Host "LakehouseId: $LakehouseId"
Write-Host "CasesPath: $CasesPath"
Write-Host "OneLake endpoint: $OneLakeEndpoint"

function Get-BronzeCategory {
    param([string]$FileName)

    if ($FileName -like 'POS-*')      { return '01_pos_transactions' }
    if ($FileName -like 'SUP-*')      { return '02_supplier_data' }
    if ($FileName -like 'PROMO-*')    { return '03_promotions' }
    if ($FileName -like 'INV-*')      { return '04_inventory' }

    throw "Unknown bronze category for file: $FileName"
}

$casesRoot = (Resolve-Path -LiteralPath $CasesPath).ProviderPath
if (-not $casesRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $casesRoot += [System.IO.Path]::DirectorySeparatorChar
}

$caseFolders = Get-ChildItem -Path $casesRoot -Directory | Sort-Object -Property Name
if ($caseFolders.Count -eq 0) {
    throw "No case folders found in $casesRoot"
}

$allFiles = @()
foreach ($case in $caseFolders) {
    $prereqPath = Join-Path $case.FullName 'fabric-pre-requisite-data'
    if (-not (Test-Path -LiteralPath $prereqPath)) {
        Write-Warning "Skipping case '$($case.Name)' — no fabric-pre-requisite-data/ folder"
        continue
    }

    $caseFiles = Get-ChildItem -Path $prereqPath -File -Recurse | Sort-Object -Property FullName
    foreach ($f in $caseFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($f.FullName)
        $category = Get-BronzeCategory -FileName $f.Name

        $allFiles += [pscustomobject]@{
            FullName     = $fullPath
            CaseName     = $case.Name
            RelativePath = "$category/$($f.Name)"
        }
    }
}

if ($allFiles.Count -eq 0) {
    throw "No pre-requisite files found in $casesRoot"
}

Write-Host "Found $($allFiles.Count) bronze files across $($caseFolders.Count) case(s)."

$token = Get-OneLakeAccessToken

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $allFiles | ForEach-Object -ThrottleLimit 10 -Parallel {
        $file = $_
        $wsId = $using:WorkspaceId
        $lhId = $using:LakehouseId
        $endpoint = $using:OneLakeEndpoint
        $token = $using:token

        $targetPath = "$lhId/Files/bronze/$($file.RelativePath)"
        $baseUri = "$endpoint/$wsId/$targetPath"
        $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)

        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        try {
            $bearer = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)

            $create = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$baseUri`?resource=file")
            $create.Headers.Authorization = $bearer
            $resp = $client.SendAsync($create).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Create failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            $content = [System.Net.Http.ByteArrayContent]::new($fileBytes)
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
            $content.Headers.ContentLength = $fileBytes.LongLength

            $append = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=append&position=0")
            $append.Headers.Authorization = $bearer
            $append.Content = $content
            $resp = $client.SendAsync($append).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Append failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            $flush = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=flush&position=$($fileBytes.LongLength)")
            $flush.Headers.Authorization = $bearer
            $resp = $client.SendAsync($flush).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Flush failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            Write-Host "Uploaded: $($file.RelativePath)"
        }
        catch {
            throw "Upload failed for '$($file.FullName)': $($_.Exception.Message) [URI: $baseUri]"
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }
}
else {
    Write-Host 'PowerShell 5.x detected. Uploading bronze files sequentially (parallel mode requires PowerShell 7+).'

    foreach ($file in $allFiles) {
            $targetPath = "$LakehouseId/Files/bronze/$($file.RelativePath)"
            $baseUri = "$OneLakeEndpoint/$WorkspaceId/$targetPath"
            $fileBytes = if ($null -eq $file.FullName) { [byte[]]@() } else { [System.IO.File]::ReadAllBytes($file.FullName) }

        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        try {
            $bearer = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)

            $create = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Put, "$baseUri`?resource=file")
            $create.Headers.Authorization = $bearer
            $resp = $client.SendAsync($create).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Create failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            $content = [System.Net.Http.ByteArrayContent]::new($fileBytes)
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
            $content.Headers.ContentLength = $fileBytes.LongLength

            $append = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=append&position=0")
            $append.Headers.Authorization = $bearer
            $append.Content = $content
            $resp = $client.SendAsync($append).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Append failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            $flush = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Patch, "$baseUri`?action=flush&position=$($fileBytes.LongLength)")
            $flush.Headers.Authorization = $bearer
            $resp = $client.SendAsync($flush).GetAwaiter().GetResult()
            if (-not $resp.IsSuccessStatusCode) {
                $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "Flush failed for '$($file.RelativePath)' ($($resp.StatusCode)): $body"
            }

            Write-Host "Uploaded: $($file.RelativePath)"
        }
        catch {
            throw "Upload failed for '$($file.FullName)': $($_.Exception.Message) [URI: $baseUri]"
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }
}

Write-Host "Bronze upload completed. Files uploaded: $($allFiles.Count)"
exit 0
