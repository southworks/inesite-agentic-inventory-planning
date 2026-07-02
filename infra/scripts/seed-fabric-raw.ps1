param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceId,

    [Parameter(Mandatory = $true)]
    [string]$LakehouseId,

    [Parameter(Mandatory = $true)]
    [string]$CasesPath,

    [string]$CorpusPath = '',

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

Write-Host '=== Fabric raw seed (Inventory Planning) ==='
Write-Host "WorkspaceId: $WorkspaceId"
Write-Host "LakehouseId: $LakehouseId"
Write-Host "CasesPath: $CasesPath"
Write-Host "OneLake endpoint: $OneLakeEndpoint"

$casesRoot = (Resolve-Path -LiteralPath $CasesPath).ProviderPath
if (-not $casesRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $casesRoot += [System.IO.Path]::DirectorySeparatorChar
}

function Get-FileExtension {
    param([string]$FileName)
    $ext = [System.IO.Path]::GetExtension($FileName).TrimStart('.').ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($ext)) { throw "Cannot determine extension for: $FileName" }
    return $ext
}

function Get-SourceFolder {
    param(
        [string]$FileName,
        [string]$CorpusSubFolder = ''
    )

    if ($CorpusSubFolder) { return $CorpusSubFolder }

    if ($FileName -like 'pos_export_*')   { return 'pos_transactions' }
    if ($FileName -like 'supplier_*')     { return 'supplier_data' }
    if ($FileName -like 'promo_*')        { return 'promotions' }
    if ($FileName -like 'inventory_*')    { return 'inventory_snapshots' }

    throw "Unknown source type for file: $FileName"
}

$caseFolders = Get-ChildItem -Path $casesRoot -Directory | Sort-Object -Property Name
if ($caseFolders.Count -eq 0) {
    throw "No case folders found in $casesRoot"
}

$allFiles = @()
foreach ($case in $caseFolders) {
    $ingestPath = Join-Path $case.FullName 'ingest'
    if (-not (Test-Path -LiteralPath $ingestPath)) {
        Write-Warning "Skipping case '$($case.Name)' — no ingest/ folder"
        continue
    }

    $caseFiles = Get-ChildItem -Path $ingestPath -File | Sort-Object -Property Name
    foreach ($f in $caseFiles) {
        $fileType = Get-FileExtension -FileName $f.Name
        $sourceFolder = Get-SourceFolder -FileName $f.Name
        $allFiles += [pscustomobject]@{
            FullName     = $f.FullName
            FileType     = $fileType
            SourceFolder = $sourceFolder
            FileName     = $f.Name
            RelativePath = "$fileType/$sourceFolder/$($f.Name)"
        }
    }
}

if ($CorpusPath) {
    $excludeNames = @('source_catalog.json', 'raw_manifest.json', 'agent_document_manifest.json', 'm5_extract.json')

    $corpusRoot = (Resolve-Path -LiteralPath $CorpusPath).ProviderPath
    if (-not $corpusRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $corpusRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    Write-Host "Corpus root: $corpusRoot"

    $corpusFiles = Get-ChildItem -Path $corpusRoot -File -Recurse |
        Where-Object {
            if ($excludeNames -contains $_.Name) { return $false }
            $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
            $segments = $fullPath.Substring($corpusRoot.Length).Split([System.IO.Path]::DirectorySeparatorChar, [System.StringSplitOptions]::RemoveEmptyEntries)
            if ($segments -contains 'agent_inputs') { return $false }
            return $true
        } |
        Sort-Object -Property FullName

    Write-Host "Found $($corpusFiles.Count) files in corpus."

    foreach ($f in $corpusFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($f.FullName)
        $relativeFromCorpus = $fullPath.Substring($corpusRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [char]'/').Replace('\', '/')
        $subFolder = $relativeFromCorpus.Split('/')[0]
        $fileType = Get-FileExtension -FileName $f.Name
        $sourceFolder = Get-SourceFolder -FileName $f.Name -CorpusSubFolder $subFolder
        $allFiles += [pscustomobject]@{
            FullName     = $fullPath
            FileType     = $fileType
            SourceFolder = $sourceFolder
            FileName     = $f.Name
            RelativePath = "$fileType/$sourceFolder/$($f.Name)"
        }
    }
}

if ($allFiles.Count -eq 0) {
    throw "No ingest files found in $casesRoot"
}

Write-Host "Found $($allFiles.Count) raw files across $($caseFolders.Count) case(s)."

$token = Get-OneLakeAccessToken

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $allFiles | ForEach-Object -ThrottleLimit 10 -Parallel {
        $file = $_
        $wsId = $using:WorkspaceId
        $lhId = $using:LakehouseId
        $endpoint = $using:OneLakeEndpoint
        $token = $using:token

        $targetPath = "$lhId/Files/raw/$($file.RelativePath)"
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
    Write-Host 'PowerShell 5.x detected. Uploading raw files sequentially (parallel mode requires PowerShell 7+).'

    foreach ($file in $allFiles) {
        $targetPath = "$LakehouseId/Files/raw/$($file.RelativePath)"
        $baseUri = "$OneLakeEndpoint/$WorkspaceId/$targetPath"
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

Write-Host "Raw upload completed. Files uploaded: $($allFiles.Count)"
exit 0
