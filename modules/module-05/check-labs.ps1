param([switch]$IncludeLocalModel)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    function Read-Lab {
        $output = & dotnet run --project lab --no-build -- @args
        if ($LASTEXITCODE -ne 0) { throw "Lab failed: $args" }
        $output | ConvertFrom-Json
    }
    function Assert-Equal($Actual, $Expected, $Name) {
        if ($Actual -cne $Expected) { throw "$Name expected [$Expected], got [$Actual]" }
        Write-Output "PASS $Name"
    }
    function Saved-Response($Id, $Run) {
        Read-Lab foundry-recording "evidence/$Id-cinematic-foundry-v3/recordings/$Run/response.json"
    }

    $tokens = Read-Lab 5.1
    Assert-Equal (($tokens | ForEach-Object { $_.count }) -join ',') '7,4,11' '5.1 token counts and exact decode round trips'
    $training = Read-Lab 5.3
    Assert-Equal $training.inference.weightsUnchanged $true '5.3 weights unchanged across inference'
    Assert-Equal $training.inference.selected 'ask for the order ID' '5.3 selected continuation'
    $app = Read-Lab 5.7
    Assert-Equal "$($app.badAccepted),$($app.correctedAccepted)" 'False,True' '5.7 wrong and corrected lookup decisions'
    Assert-Equal $app.correctedAnswer 'Order A-1042 is delayed.' '5.7 corrected display'

    $short = Saved-Response '5.2' 'm05-52-short'
    $room = Saved-Response '5.2' 'm05-52-room'
    Assert-Equal "$($short.finishReason),$($room.finishReason)" 'length,stop' '5.2 recorded finish reasons'
    Assert-Equal $short.content '' '5.2 empty visible short completion'
    Assert-Equal @($room.content -split '\r?\n' | Where-Object {$_ -match '^\d+\.\s'}).Count 12 '5.2 recorded greetings'

    $ids = foreach ($case in 'full','trimmed','restored') {
        (Saved-Response '5.4' "m05-54-$case").content
    }
    Assert-Equal ($ids -join ',') 'A-1042,MISSING,A-1042' '5.4 recorded context outcomes'
    Assert-Equal (Saved-Response '5.4' 'm05-csharp-reproduction-001').content 'A-1042' '5.4 recorded compiled C# confirmation'
    foreach ($case in 'known','ambiguous','missing','supplied') {
        $answer = (Saved-Response '5.5' "m05-55-$case").content
        $pattern = @{known='30 days.*delivery';ambiguous='final-sale.*delivered\?';missing='does not specify';supplied='USD 12'}[$case]
        Assert-Equal ($answer -match $pattern) $true "5.5 policy $case"
    }
    $low = Saved-Response '5.6' 'm05-56-low'
    $medium = Saved-Response '5.6' 'm05-56-medium'
    Assert-Equal ($low.scheduleValid -and $medium.scheduleValid -and $low.content -ceq $medium.content) $true '5.6 both recorded schedules valid and equal'
    Assert-Equal "$($low.usage.completion_tokens_details.reasoning_tokens),$($medium.usage.completion_tokens_details.reasoning_tokens)" '128,256' '5.6 recorded reasoning tokens'
    Assert-Equal (Read-Lab foundry-recording exercises/5.6/starter-response.json).scheduleValid $false 'exercise rejects duplicate slot'
    Assert-Equal (Read-Lab foundry-recording exercises/5.6/completed-response.json).scheduleValid $true 'exercise accepts repaired schedule'

    if ($IncludeLocalModel) {
        $hash = (Get-FileHash tools/smollm2.gguf).Hash.ToLowerInvariant()
        Assert-Equal $hash 'e55c2a5df7f8f6eed1a843ff709fa8008dbce65d82e57c5c06dec5ee8d060415' 'optional local model integrity'
        $decode = Read-Lab 5.2
        Assert-Equal ($decode.samples.Count -eq 3 -and [Math]::Abs($decode.probabilities[0] - .66524) -lt .00001) $true '5.2 toy softmax and three local generations'
        $context = Read-Lab 5.4
        Assert-Equal "$($context.appLookup.full),$($context.appLookup.trimmed),$($context.appLookup.restored)" 'A-1042,,A-1042' '5.4 local application state'
        Assert-Equal (Read-Lab 5.5).responses.Count 3 '5.5 local policy responses'
        Assert-Equal (Read-Lab 5.6).verified 'Ava=11, Ben=09, Chen=10' '5.6 local deterministic schedule verifier'
    }
} finally {
    Pop-Location
}

