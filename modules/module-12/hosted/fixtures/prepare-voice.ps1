# Recreate the synthetic agreement/conflict pair on Windows PowerShell 5.1.
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $speaker.SelectVoice('Microsoft Zira Desktop')
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,
        [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono)
    $prompts = [ordered]@{
        agreement = 'The handle on my new kettle is cracked. The order number is zero four eight two seven one.'
        conflict = 'The handle on my new kettle is cracked. The order number is zero four eight two seven two.'
        unclear = 'The handle on my kettle is cracked. I do not have the full order number.'
    }
    foreach ($variant in $prompts.Keys) {
        $file = Join-Path $PSScriptRoot "request-$variant.wav"
        if (Test-Path -LiteralPath $file) { continue }
        $speaker.SetOutputToWaveFile($file, $format)
        $speaker.Speak($prompts[$variant])
        $speaker.SetOutputToNull()
    }
}
finally { $speaker.Dispose() }
