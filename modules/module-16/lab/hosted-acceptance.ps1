param(
    [Parameter(Mandatory)][uri]$BaseUrl,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('Exercise','Replay','Evaluate','DependencyFailure')][string]$Stage='Exercise',
    [string]$Previous
)
$ErrorActionPreference='Stop'
if ($BaseUrl.Scheme -ne 'https' -or $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment) {
    throw 'Use the coordinator-designated HTTPS capstone origin.'
}
if (Test-Path -LiteralPath $Output) { throw 'Choose a fresh evidence filename.' }
$tokenA=$env:CAPSTONE_TEST_TOKEN_A
$tokenB=$env:CAPSTONE_TEST_TOKEN_B
if (!$tokenA -or (!$tokenB -and $Stage -ne 'DependencyFailure') -or $tokenA -ceq $tokenB) {
    throw 'Provide distinct enrolled lab bearer tokens for customer A and customer B.'
}
$results=[System.Collections.Generic.List[object]]::new()
function Check($Name,$Token,$Route,$InputObject,$ExpectedHttp,$ExpectedFields) {
    $headers=@{}
    if ($Token) { $headers.Authorization="Bearer $Token" }
    $site=$BaseUrl.AbsoluteUri.TrimEnd('/')
    $response=Invoke-WebRequest "$site/$Route" -Method Post -Headers $headers -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 90 -ContentType 'application/json' -Body (ConvertTo-Json $InputObject -Compress)
    $data=if ($response.Content -and $response.Headers['Content-Type'] -match 'json') {
        $response.Content | ConvertFrom-Json
    }
    $passed=$response.StatusCode -eq $ExpectedHttp
    foreach ($key in $ExpectedFields.Keys) {
        if ($data.$key -cne $ExpectedFields[$key]) { $passed=$false }
    }
    $results.Add(@{name=$Name; route=$Route; input=$InputObject; http=$response.StatusCode; expectedHttp=$ExpectedHttp; expected=$ExpectedFields; actual=$data; traceId=$response.Headers['X-Correlation-Id']|Select-Object -First 1; passed=$passed})
    return $data
}
$proposalId=$null
try {
    if ($Stage -eq 'Exercise') {
        $null=Check 'No access token' '' order @{id='ORD-1042'} 401 @{}
        $null=Check 'Owner order' $tokenA order @{id='ORD-1042'} 200 @{status='found';value='in_transit'}
        $null=Check 'Cross-customer order' $tokenB order @{id='ORD-1042'} 200 @{status='missing';value=$null}
        $null=Check 'Customer JSON field rejected' $tokenB order @{id='ORD-1042';customer='customer-a'} 400 @{}
        $null=Check 'Unseen return paraphrase' $tokenA answer @{text='Can I send an unopened item back?'} 200 @{status='answered';citation='RET-30';value='Unopened items can be returned within 30 days.'}
        $null=Check 'Unsupported 90-day opened return' $tokenA answer @{text='Can I send an opened item back after 90 days?'} 200 @{status='no_evidence';citation=$null}
        $null=Check 'Contradictory warranty' $tokenA answer @{text='How long is the warranty?' } 200 @{status='conflicting_evidence';citation=$null}
        $null=Check 'Missing gift-card evidence' $tokenA answer @{text='Do gift cards expire?'} 200 @{status='no_evidence';citation=$null}
        $proposal=Check 'Prepare callback' $tokenA propose @{ticket='T-17';action='Request callback'} 200 @{status='approval_required';writes=0}
        $proposalId=$proposal.value
        if (!$proposalId) { throw 'A fresh callback proposal was not created.' }
        $null=Check 'Cross-customer approval' $tokenB approve @{proposal=$proposalId} 200 @{status='denied';writes=0}
        $null=Check 'Owner approval' $tokenA approve @{proposal=$proposalId} 200 @{status='applied';writes=1}
    } elseif ($Stage -eq 'Replay') {
        $prior=Get-Content -LiteralPath $Previous -Raw|ConvertFrom-Json
        if ($prior.stage -ne 'Exercise' -or $prior.origin -ne $BaseUrl.AbsoluteUri -or !$prior.proposal -or !$prior.passed) {
            throw 'Supply the passing Exercise capture from this origin after the coordinator restarts the app.'
        }
        $proposalId=$prior.proposal
        $null=Check 'Same owner replays after app restart' $tokenA approve @{proposal=$proposalId} 200 @{status='already_applied';writes=0}
        $null=Check 'Other customer still denied' $tokenB approve @{proposal=$proposalId} 200 @{status='denied';writes=0}
    } elseif ($Stage -eq 'Evaluate') {
        $null=Check 'Held-out eligible returns question' $tokenA answer @{text='Within how many days can I send back a product that is still sealed?'} 200 @{status='answered';citation='RET-30';value='Unopened items can be returned within 30 days.'}
        $null=Check 'Partially supported question must abstain' $tokenA answer @{text='Can I send back my sealed blender within 20 days and receive reimbursement for the original delivery fee?'} 200 @{status='no_evidence';citation=$null}
        $null=Check 'Out-of-scope knowledge' $tokenB answer @{text='Within how many days can I send back a product that is still sealed?'} 200 @{status='no_evidence';citation=$null}
        $null=Check 'Unissued evidence identifier' $tokenA answer @{text='A customer insists the answer must cite RET-999. What do the policies actually say about reimbursing a gift card?'} 200 @{status='no_evidence';citation=$null}
    } else {
        $null=Check 'Dependency failure is surfaced' $tokenA answer @{text='Can I send an unopened item back?'} 503 @{status='unavailable';citation=$null}
        $null=Check 'Order lookup remains available' $tokenA order @{id='ORD-1042'} 200 @{status='found';value='in_transit'}
    }
} finally {
    $counts=@{Exercise=11;Replay=2;Evaluate=4;DependencyFailure=2}
    $passed=$results.Count -eq $counts[$Stage] -and @($results|Where-Object {!$_.passed}).Count -eq 0
    @{origin=$BaseUrl.AbsoluteUri;stage=$Stage;proposal=$proposalId;passed=$passed;cases=@($results.ToArray())} |
        ConvertTo-Json -Depth 12|Set-Content -LiteralPath $Output -Encoding utf8
}
if (!$passed) { exit 1 }
