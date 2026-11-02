param(
    [string]$Runner,
    [string]$ModelDirectory,
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) '.build-cache/wake'),
    [switch]$GenerateOnly
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$fixtures = @(
    @('ask.wav', 'Hey sharp voice, what is the capital of Texas?', 'ask', 100),
    @('dictate.wav', 'Hey sharp voice dictate, please bring the blue folder tomorrow.', 'dictation', 100),
    @('negative.wav', 'Please bring the blue folder tomorrow.', 'none', 100),
    @('quiet-ask.wav', 'Hey sharp voice, what is the capital of Texas?', 'ask', 20),
    @('quiet-dictate.wav', 'Hey sharp voice dictate, please bring the blue folder tomorrow.', 'dictation', 20),
    @('phrase-only.wav', 'Hey sharp voice.', 'ask', 100)
)
try {
    foreach ($fixture in $fixtures) {
        $synth.Volume = $fixture[3]
        $synth.SetOutputToWaveFile((Join-Path $OutputDirectory $fixture[0]), $format)
        $synth.Speak($fixture[1])
        $synth.SetOutputToNull()
    }
}
finally { $synth.Dispose() }
if ($GenerateOnly) { return }
if (-not $Runner -or -not $ModelDirectory) { throw 'Provide -Runner and -ModelDirectory, or use -GenerateOnly.' }
foreach ($fixture in $fixtures) {
    $nativeArgs = @('--wake-native', $ModelDirectory, (Join-Path $OutputDirectory $fixture[0]), 'Hey SharpVoice Dictate', 'Hey SharpVoice', $fixture[2])
    $quotedArgs = $nativeArgs | ForEach-Object { if ($_ -match '["\r\n]') { throw 'Invalid fixture argument.' }; '"' + $_ + '"' }
    $process = Start-Process -FilePath $Runner -ArgumentList $quotedArgs -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ('Wake fixture failed: ' + $fixture[0] + ' (exit ' + $process.ExitCode + ')') }
}
