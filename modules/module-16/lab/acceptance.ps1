param([string]$BaseUrl = 'http://127.0.0.1:8416', [string]$Output, [switch]$Upstream, [switch]$InvalidDraft)
$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[object]]::new()

function Check($Name, $Route, $InputObject, $Expected) {
    $body = ConvertTo-Json -InputObject $InputObject -Compress
    $reply = Invoke-RestMethod "$BaseUrl/$Route" -Method Post -ContentType application/json -Body $body -ResponseHeadersVariable headers
    $passed = $true
    foreach ($field in $Expected.Keys) {
        if ($reply.$field -cne $Expected[$field]) { $passed = $false }
    }
    $results.Add(@{ name = $Name; route = $Route; input = $InputObject; expected = $Expected; actual = $reply; traceId = $headers['X-Correlation-Id'] | Select-Object -First 1; pass = $passed })
    return $reply
}

$returns = if ($InvalidDraft) {
    @{status='invalid_answer';citation=$null;value=$null}
} else {
    @{status='answered';citation='RET-30';value='Unopened items can be returned within 30 days.'}
}
$null = Check 'Policy answer and citation' answer @{topic='returns';text='Can I return an unopened item?'} $returns
$null = Check 'Ambiguous request' answer @{topic='returns';text=''} @{status='clarify';value=$null}
$null = Check 'Unanswerable policy' answer @{topic='gift-cards';text='Do gift cards expire?'} @{status='no_evidence';value=$null}
$null = Check 'Conflicting policy' answer @{topic='warranty';text='How long does the warranty last?'} @{status='conflicting_evidence';value=$null}
$null = Check 'Dependency outage fixture' answer @{topic='outage';text='Can I make a return?'} @{status='unavailable';value=$null}
$null = Check 'Order owner' order @{customer='customer-a';id='O-204'} @{status='found';value='Shipped'}
$null = Check 'Different customer' order @{customer='customer-b';id='O-204'} @{status='denied';value=$null}
$null = Check 'Missing order' order @{customer='customer-a';id='O-999'} @{status='missing';value=$null}
$proposal = Check 'Propose change without writing' propose @{customer='customer-a';ticket='T-17';action='Request callback'} @{status='approval_required';writes=0}
$null = Check 'Approval by wrong customer' approve @{customer='customer-b';proposal=$proposal.value} @{status='denied';writes=0}
$null = Check 'Approved change' approve @{customer='customer-a';proposal=$proposal.value} @{status='applied';writes=1}
$null = Check 'Approval replay' approve @{customer='customer-a';proposal=$proposal.value} @{status='already_applied';writes=0}
$null = Check 'Route a question without a topic' answer @{text='Can I return an unopened item?'} $returns
$null = Check 'Request outside supported topics' answer @{text='Can I book a meeting room?'} @{status='no_evidence';value=$null;citation=$null}
if ($Upstream) {
    $null = Check 'Module 1 order returned' order @{customer='customer-a';id='ORD-1042'} @{status='found';value='in_transit'}
    $null = Check 'Module 1 order denied to another customer' order @{customer='customer-b';id='ORD-1042'} @{status='denied';value=$null}
    $null = Check 'Module 1 missing order' order @{customer='customer-a';id='ORD-9999'} @{status='missing';value=$null}
    $null = Check 'Chat fixture ID cannot identify a starter order' order @{customer='customer-a';id='A-103'} @{status='missing';value=$null}
    $null = Check 'Receipt-like digits cannot identify a starter order' order @{customer='customer-a';id='1042'} @{status='missing';value=$null}
}
$failed = @($results | Where-Object { !$_.pass }).Count
$health = Invoke-RestMethod "$BaseUrl/health"
$report = @{ mode=$health.mode; total=$results.Count; failed=$failed; passed=$results.Count-$failed; cases=$results }
$json = ConvertTo-Json $report -Depth 12
if ($Output) { $json | Set-Content -Encoding utf8 $Output }
$json
if ($failed) { exit 1 }
