param([string]$Tests = "$PSScriptRoot/../artifacts/tests-aot/SharpVoice.Tests.exe", [string]$Output = "$PSScriptRoot/../artifacts/performance.txt")
$ErrorActionPreference = 'Stop'
$Tests = (Resolve-Path -LiteralPath $Tests).Path
$root = Split-Path $PSScriptRoot -Parent
$results = @("SharpVoice performance measurements: $(Get-Date -Format o)", "CPU: $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name)", "OS: $([Environment]::OSVersion.VersionString)")
function Run-Check([string[]]$Arguments) {
    $script:results += "Command: $($Arguments -join ' ')"
    $stdout = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N') + '.out')
    $stderr = "$stdout.err"
    try {
        $quoted = ($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
        $child = Start-Process -FilePath $Tests -ArgumentList $quoted -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $code = $child.ExitCode
        $script:results += Get-Content $stdout
        $script:results += Get-Content $stderr
    }
    finally { Remove-Item $stdout, $stderr -ErrorAction SilentlyContinue }
    if ($code -ne 0) { $script:results += "Exit code: $code"; $script:results | Set-Content $Output; throw "Performance check failed: $Arguments (exit $code)" }
}
Run-Check @('--windows-performance-smoke')
$model = Join-Path $env:USERPROFILE '.sharpvoice/models/ggml-tiny.en-q5_1.bin'
$fixture = Join-Path $root '.build-cache/fixtures/sharpvoice-jfk.wav'
if ((Test-Path $model) -and (Test-Path $fixture)) { Run-Check @('--whisper-smoke', $model, $fixture) }
$results | Set-Content $Output
$results
