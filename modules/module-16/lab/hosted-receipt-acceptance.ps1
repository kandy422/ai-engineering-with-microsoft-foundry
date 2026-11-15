param(
    [Parameter(Mandatory)][uri]$BaseUrl,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('All','ChangedReference')][string]$CaseSet = 'All'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($BaseUrl.Scheme -ne 'https' -or !$BaseUrl.IsDefaultPort -or $BaseUrl.AbsolutePath -ne '/' -or
    $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment -or
    $BaseUrl.Host -notmatch '^[a-z0-9-]+--[a-z0-9-]+\.[a-z0-9.-]+\.azurecontainerapps\.io$') {
    throw 'Supply a direct HTTPS Azure Container Apps revision origin.'
}
if (Test-Path -LiteralPath $Output) { throw 'Choose a fresh evidence filename.' }
if (!$env:CAPSTONE_TEST_TOKEN_A -or ($CaseSet -eq 'All' -and
    (!$env:CAPSTONE_TEST_TOKEN_B -or $env:CAPSTONE_TEST_TOKEN_A -ceq $env:CAPSTONE_TEST_TOKEN_B))) {
    throw 'Supply the short-lived enrolled workload tokens required by this case set through the environment.'
}
$tokens = @{ 'customer-a' = ConvertTo-SecureString $env:CAPSTONE_TEST_TOKEN_A -AsPlainText -Force }
if ($CaseSet -eq 'All') {
    $tokens['customer-b'] = ConvertTo-SecureString $env:CAPSTONE_TEST_TOKEN_B -AsPlainText -Force
}
$tests = @(
    @{ name='scoped receipt match'; actor='customer-a'; fixture='lumen-receipt.png'; status='receipt_matched' }
    @{ name='cross-customer receipt denial'; actor='customer-b'; fixture='lumen-receipt.png'; status='denied' }
    @{ name='changed reference withheld'; actor='customer-a'; fixture='lumen-receipt-reference-mismatch.png'; status='receipt_review' }
)
if ($CaseSet -eq 'ChangedReference') { $tests = @($tests[-1]) }
$endpoint = [uri]::new($BaseUrl,'attachment')
$root = Join-Path (Split-Path $PSScriptRoot) 'tests/fixtures'
$receipts = [Collections.Generic.List[object]]::new()
$attempted = 0
try {
    foreach ($case in $tests) {
        $file = Get-Item -LiteralPath (Join-Path $root $case.fixture)
        if ($file.Length -le 0 -or $file.Length -gt 100000) { throw "Invalid size for $($file.Name)." }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $started = [DateTimeOffset]::UtcNow
        $attempted++
        $response = Invoke-WebRequest -Uri $endpoint -Method Post -Authentication Bearer -Token $tokens[$case.actor] `
            -Headers @{'X-Order-Id'='ORD-1042'} -ContentType 'image/png' -InFile $file.FullName `
            -MaximumRedirection 0 -MaximumRetryCount 0 -SkipHttpErrorCheck -TimeoutSec 75
        $body = [string]$response.Content
        $data = try { $body | ConvertFrom-Json -AsHashtable } catch { $null }
        $correlation = $response.Headers['X-Correlation-Id'] | Select-Object -First 1
        $valueMatches = if ($case.status -eq 'receipt_matched') {
            $data.value -ceq 'Order ORD-1042 matches receipt reference 048271. Current order status: in_transit.'
        } else {
            $null -eq $data.value -and $body -notmatch 'ORD-1042|04827[12]|in_transit'
        }
        $passed = $response.StatusCode -eq 200 -and $data.status -ceq $case.status -and
            $valueMatches -and $null -eq $data.citation -and $data.writes -eq 0 -and $correlation -cmatch '^[a-f0-9]{32}$'
        $receipts.Add([ordered]@{
            name=$case.name; actor=$case.actor; startedUtc=$started.ToString('o')
            finishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
            request=@{method='POST'; url=$endpoint.AbsoluteUri; contentType='image/png'; orderId='ORD-1042'
                fixture=$case.fixture; bytes=$file.Length; sha256=$hash}
            expectedStatus=$case.status
            http=$response.StatusCode
            correlationId=$correlation
            responseBody=$body
            responseSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($body))).ToLowerInvariant()
            passed=$passed
        })
        Write-Host "$($case.name): HTTP $($response.StatusCode), $($data.status), pass=$passed"
        if ($response.StatusCode -ne 200) { break }
    }
} finally {
    $allPassed = $receipts.Count -eq $tests.Count -and @($receipts | Where-Object { !$_.passed }).Count -eq 0
    @{
        origin=$BaseUrl.AbsoluteUri; attempts=$attempted; maximumAnalyzePagesPerAttempt=1
        expectedAnalysisCalls=@($tests | Where-Object {$_.actor -eq 'customer-a'}).Count; measuredAnalysisCalls=$null
        passed=$allPassed; cases=@($receipts.ToArray())
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Output -Encoding utf8
}
if (!$allPassed) { exit 1 }
