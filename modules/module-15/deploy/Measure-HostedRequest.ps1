#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [uri] $RevisionUrl,
    [Parameter(Mandatory)] [securestring] $AccessToken,
    [Parameter(Mandatory)] [string] $Question,
    [ValidateSet(200, 503)] [int] $ExpectedStatus = 200,
    [ValidateRange(1, 5)] [int] $Requests = 1
)
$ErrorActionPreference = 'Stop'
if ($RevisionUrl.Scheme -ne 'https' -or !$RevisionUrl.IsDefaultPort -or
    !$RevisionUrl.DnsSafeHost.EndsWith('.azurecontainerapps.io') -or $RevisionUrl.AbsolutePath -ne '/' -or
    $RevisionUrl.UserInfo -or $RevisionUrl.Query -or $RevisionUrl.Fragment) {
    throw 'Provide the HTTPS Azure Container Apps revision origin.'
}
$target = [uri]::new($RevisionUrl, '/answer')
$body = @{ text = $Question } | ConvertTo-Json -Compress
for ($i = 1; $i -le $Requests; $i++) {
    $trace = [guid]::NewGuid().ToString('N')
    $span = [guid]::NewGuid().ToString('N').Substring(0,16)
    $time = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-WebRequest -Uri $target -Method Post -Body $body -ContentType 'application/json' `
        -Authentication Bearer -Token $AccessToken -Headers @{ traceparent = "00-$trace-$span-01" } `
        -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 45
    $time.Stop()
    $result = $response.Content | ConvertFrom-Json
    if ([string] ($response.Headers['X-Correlation-Id'] | Select-Object -First 1) -ne $trace) {
        throw "The deployed response did not confirm request trace $trace."
    }
    if ($response.StatusCode -ne $ExpectedStatus -or ($ExpectedStatus -eq 503 -and $result.status -ne 'unavailable')) {
        throw "Unexpected response $($response.StatusCode) / $($result.status) on attempt $i. Trace $trace."
    }
    [pscustomobject]@{
        utc = [DateTimeOffset]::UtcNow.ToString('o')
        host = $RevisionUrl.DnsSafeHost
        traceId = $trace
        case = $i
        statusCode = [int] $response.StatusCode
        outcome = $result.status
        elapsedMs = $time.ElapsedMilliseconds
        citation = $result.citation
    }
}
