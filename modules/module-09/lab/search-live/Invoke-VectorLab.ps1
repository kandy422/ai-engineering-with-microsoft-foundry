[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$Endpoint,
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidateSet('Create','Load','Query','Negative','Cleanup')][string]$Action
)
$ErrorActionPreference = 'Stop'
if ($Endpoint.Scheme -ne 'https' -or $Endpoint.Host -notmatch '^[a-z0-9-]+\.search\.windows\.net$' -or
    $Endpoint.AbsolutePath -ne '/' -or $Endpoint.Query -or $Endpoint.Fragment -or $Endpoint.UserInfo -or
    -not $Endpoint.IsDefaultPort) { throw 'Use an Azure AI Search service base URL.' }
$requests = Get-Content -LiteralPath $Payload -Raw | ConvertFrom-Json -AsHashtable
$index = $requests.schema.name
if ($index -notmatch '^course09-[a-z0-9]+(?:-[a-z0-9]+)*$' -or $index.Length -gt 128 -or
    $requests.apiVersion -cne '2025-09-01') { throw 'Invalid dedicated index name or API version.' }
$token = az account get-access-token --scope https://search.azure.com/.default --query accessToken -o tsv --only-show-errors
if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'Could not acquire a Search Entra token.' }
$null = New-Item -ItemType Directory -Path $EvidenceDirectory -Force
$base = $Endpoint.AbsoluteUri.TrimEnd('/')
$version = $requests.apiVersion

function Invoke-RecordedSearch([string]$Method, [string]$Route, [object]$Body, [int]$Attempt) {
    $requestId = [guid]::NewGuid().ToString()
    $uri = "$base/${Route}?api-version=$version"
    $params = @{
        Method = $Method; Uri = $uri; TimeoutSec = 25; SkipHttpErrorCheck = $true
        Headers = @{ Authorization = "Bearer $token"; 'client-request-id' = $requestId; 'return-client-request-id' = 'true' }
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = ConvertTo-Json -InputObject $Body -Depth 12 -Compress
    }
    $response = Invoke-WebRequest @params
    $record = [ordered]@{
        utc = [datetime]::UtcNow.ToString('o'); method = $Method; uri = $uri
        clientRequestId = $requestId; serviceRequestId = @($response.Headers['request-id']) -join ','
        status = [int]$response.StatusCode; embeddingRequestId = $requests.embeddingRequestId
        payloadSha256 = (Get-FileHash -LiteralPath $Payload -Algorithm SHA256).Hash.ToLowerInvariant()
        response = if ($response.Content) { ConvertFrom-Json -InputObject $response.Content -AsHashtable }
    }
    $path = Join-Path $EvidenceDirectory "$Action-$Attempt-$requestId.json"
    $record | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Host "$Method $Route -> HTTP $($record.status)  ($path)"
    return $record
}

function Assert-Keys($Actual, $Expected) {
    if (@($Actual).Count -ne @($Expected).Count -or
        (@($Actual | Sort-Object) -join ',') -cne (@($Expected | Sort-Object) -join ',')) {
        throw "Unexpected keys: $($Actual -join ', ')"
    }
}

switch ($Action) {
    'Create' {
        $r = Invoke-RecordedSearch POST 'indexes' $requests.schema 1
        $field = @($r.response.fields | Where-Object name -CEQ 'contentVector')
        if ($r.status -ne 201 -or $r.response.name -cne $index -or
            $field.Count -ne 1 -or $field[0].dimensions -ne 1536) { throw 'Index creation or schema verification failed.' }
    }
    'Load' {
        $r = Invoke-RecordedSearch POST "indexes('$index')/docs/search.index" $requests.upload 1
        if ($r.status -ne 200 -or @($r.response.value | Where-Object { $_.status -ne $true -or $_.statusCode -notin 200,201 }).Count) {
            throw 'Indexing failed or returned partial success. Inspect the individual document statuses.'
        }
        Assert-Keys @($r.response.value.key) @($requests.upload.value.id)
        $r.response.value | ForEach-Object { [pscustomobject]$_ } | Format-Table key, status, statusCode
    }
    'Query' {
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            $r = Invoke-RecordedSearch POST "indexes('$index')/docs/search.post.search" $requests.query $attempt
            if ($r.status -ne 200) { throw 'Search query failed.' }
            $hits = @($r.response.value)
            foreach ($hit in $hits) {
                if ($hit.tenantId -cne 'tenant-a' -or $hit.customerId -cne 'customer-a' -or
                    $hit.product -cne 'laptop' -or $hit.isCurrent -ne $true -or
                    $hit.id -cnotin $requests.expectedKeys -or -not $hit.content) { throw 'Unauthorized or malformed search hit.' }
            }
            if ($hits.Count -eq @($requests.expectedKeys).Count) { break }
            if ($attempt -lt 6) { Start-Sleep -Seconds 3 }
        }
        Assert-Keys @($hits | ForEach-Object { $_.id }) $requests.expectedKeys
        $hits | ForEach-Object { [pscustomobject]$_ } | Format-Table id, policyVersion, product, content -Wrap
    }
    'Negative' {
        $r = Invoke-RecordedSearch POST "indexes('$index')/docs/search.post.search" $requests.negative 1
        if ($r.status -ne 200 -or -not $r.response.Contains('value') -or @($r.response.value).Count -ne 0) {
            throw 'Expected HTTP 200 with no eligible documents for the negative scope.'
        }
        Write-Host 'No eligible documents. Application should abstain from answering with policy evidence.'
    }
    'Cleanup' {
        $r = Invoke-RecordedSearch DELETE "indexes('$index')" $null 1
        if ($r.status -notin 204,404) { throw 'Index removal failed.' }
        $r = Invoke-RecordedSearch GET "indexes('$index')" $null 2
        if ($r.status -ne 404) { throw 'Index still exists.' }
    }
}
