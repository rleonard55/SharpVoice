param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.12',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
if (-not (Test-Path $dotnet)) { throw 'Install the .NET 10 SDK first.' }
$work = Join-Path $root ('.build-cache/release-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$packages = Join-Path $root '.build-cache/nuget'
$output = Join-Path $root ('artifacts/releases/' + $Version)
New-Item -ItemType Directory -Path $work,$output -Force | Out-Null

function Quote-Arg([string]$value) {
    if ($value.Length -gt 0 -and $value -notmatch '[\s"]') { return $value }
    '"' + [regex]::Replace([regex]::Replace($value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}
function Run-Tool([string]$file, [string[]]$arguments, [string]$log) {
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = $file
    $process.StartInfo.Arguments = (($arguments | ForEach-Object { Quote-Arg $_ }) -join ' ')
    $process.StartInfo.WorkingDirectory = $root
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.EnvironmentVariables['PATHEXT'] = '.COM;.EXE;.BAT;.CMD'
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [IO.File]::WriteAllText($log, $stdout.Result + $stderr.Result)
    if ($process.ExitCode -ne 0) { throw "Command failed ($($process.ExitCode)). See $log" }
    return $stdout.Result
}
Push-Location $root
try {
    if (-not $SkipTests) {
        Write-Host 'Building and running offline checks...'
        $null = Run-Tool $dotnet @('build','tests/SharpVoice.Tests/SharpVoice.Tests.csproj','-c','Release','--artifacts-path','.build-cache/windows',"-p:RestorePackagesPath=$packages",'-p:UseSharedCompilation=false','-m:1','--nologo') (Join-Path $work 'tests-build.log')
        $checks = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @() (Join-Path $work 'tests.log')
        if ($checks -notmatch '(\d+)/\1 checks passed\.') { throw 'The complete offline suite did not pass.' }
        $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--windows-hud-window-smoke') (Join-Path $work 'hud-window.log')
        $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--windows-onboarding-smoke') (Join-Path $work 'onboarding-window.log')
        $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--windows-updates-smoke') (Join-Path $work 'updates-window.log')
        $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--windows-compact-audio-smoke') (Join-Path $work 'compact-audio-window.log')
        $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--windows-provider-diagnostics-smoke',(Join-Path $work 'provider-diagnostics')) (Join-Path $work 'provider-diagnostics-window.log')
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio C++ build tools and the Windows SDK first.' }
    $vs = (Run-Tool $vswhere @('-latest','-products','*','-requires','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','-property','installationPath') (Join-Path $work 'vswhere.log')).Trim()
    if (-not $vs) { throw 'Visual Studio x64 C++ tools were not found.' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
    $publishArgs = @('publish','src/SharpVoice.App/SharpVoice.App.csproj','-c','Release','-r','win-x64','--self-contained','true','-p:PublishAot=true',"-p:SharpVoiceVersion=$Version",'-p:DebugType=None','-p:DebugSymbols=false','-p:NativeDebugSymbols=false','-p:CopyOutputSymbolsToPublishDirectory=false','-p:StripSymbols=true',"-p:PathMap=$root=/_/SharpVoice",'-p:UseSharedCompilation=false','-p:IlcUseEnvironmentalTools=true',"-p:RestorePackagesPath=$packages",'--artifacts-path','.build-cache/windows','-m:1','-o',$publish,'--nologo')
    $batch = Join-Path $work 'publish.cmd'
    $command = (Quote-Arg $dotnet) + ' ' + (($publishArgs | ForEach-Object { Quote-Arg $_ }) -join ' ')
    [IO.File]::WriteAllText($batch, "@echo off`r`nset `"PATHEXT=.COM;.EXE;.BAT;.CMD`"`r`ncall `"$vcvars`"`r`nif errorlevel 1 exit /b 1`r`n$command`r`nexit /b %errorlevel%`r`n", [Text.Encoding]::ASCII)
    Write-Host 'Publishing sanitized native Windows x64 app...'
    $null = Run-Tool $env:ComSpec @('/d','/c',$batch) (Join-Path $work 'publish.log')
    if (Get-ChildItem -LiteralPath $publish -Filter '*.pdb' -Recurse) { throw 'Release output contains debug symbols.' }
    $exe = Join-Path $publish 'SharpVoice.exe'
    $bytes = [IO.File]::ReadAllBytes($exe)
    $texts = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes,1,$bytes.Length-1))
    foreach ($privatePath in @($root,$env:USERPROFILE)) {
        foreach ($variant in @($privatePath,$privatePath.Replace('\','/'))) {
            if ($variant -and ($texts | Where-Object { $_.IndexOf($variant,[StringComparison]::OrdinalIgnoreCase) -ge 0 })) { throw 'Release executable contains a local builder path.' }
        }
    }
    $texts = $null; $bytes = $null
    $info = Join-Path $work 'release-info.json'
    $null = Run-Tool $exe @('--release-info',$info) (Join-Path $work 'bootstrap.log')
    $metadata = Get-Content -LiteralPath $info -Raw | ConvertFrom-Json
    if ($metadata.version -ne $Version -or $metadata.updates_supported) { throw 'Native release bootstrap/version verification failed.' }
    # Only the app, verified default speech model and public notices enter the package.
    $staging = Join-Path $work 'package'
    New-Item -ItemType Directory -Path $staging,(Join-Path $staging 'licenses') -Force | Out-Null
    Copy-Item -LiteralPath $exe -Destination $staging
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $staging
    Copy-Item -Path (Join-Path $root 'docs/licenses/*-LICENSE') -Destination (Join-Path $staging 'licenses')
    Copy-Item -LiteralPath (Join-Path $root 'docs/third-party-notices.md') -Destination $staging
    $modelName = 'ggml-tiny.en-q5_1.bin'
    $modelHash = 'c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b'
    $modelCache = Join-Path $root '.build-cache/release-models'
    $model = Join-Path $modelCache $modelName
    New-Item -ItemType Directory -Path $modelCache,(Join-Path $staging 'models') -Force | Out-Null
    if (-not (Test-Path $model) -or (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash -ne $modelHash) {
        $partial = Join-Path $work ($modelName + '.download')
        Write-Host 'Downloading the default Tiny Q5 English model for packaging...'
        try {
            Invoke-WebRequest -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$modelName" -OutFile $partial -TimeoutSec 300
            if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $modelHash) { throw 'Bundled Whisper model SHA-256 mismatch.' }
            Move-Item -LiteralPath $partial -Destination $model -Force
        }
        finally { if (Test-Path $partial) { Remove-Item -LiteralPath $partial -Force } }
    }
    Copy-Item -LiteralPath $model -Destination (Join-Path $staging 'models')
    $bundledInfo = Join-Path $work 'bundled-model-info.json'
    $null = Run-Tool (Join-Path $staging 'SharpVoice.exe') @('--verify-bundled-model',$bundledInfo) (Join-Path $work 'bundled-model.log')
    $bundled = Get-Content -LiteralPath $bundledInfo -Raw | ConvertFrom-Json
    if ($bundled.model -ne 'tiny.en-q5_1' -or $bundled.sha256 -ne $modelHash -or $bundled.network_requests -ne 0) { throw 'Packaged app failed offline default-model resolution.' }
    if (-not $SkipTests) {
        foreach ($activation in @('keep_ready','on_hotkey')) {
            $null = Run-Tool (Join-Path $root '.build-cache/windows/bin/SharpVoice.Tests/release/SharpVoice.Tests.exe') @('--idle-memory-app',$modelCache,$activation) (Join-Path $work "idle-memory-$activation.log")
        }
    }
    $null = Run-Tool $dotnet @('tool','restore','--tool-manifest','.config/dotnet-tools.json') (Join-Path $work 'tool-restore.log')
    $packOutput = Join-Path $work 'releases'
    Write-Host 'Building per-user installer, portable ZIP and update feed...'
    $packArgs = @('tool','run','vpk','--','pack','--packId','SharpVoice','--packVersion',$Version,'--packDir',$staging,'--mainExe','SharpVoice.exe','--packTitle','SharpVoice','--packAuthors','Rob Leonard','--runtime','win-x64','--channel','win-x64','--shortcuts','StartMenuRoot','--delta','None','--icon',(Join-Path $root 'src/SharpVoice.UI/Assets/microphone-neutral.ico'),'--releaseNotes',(Join-Path $root "docs/releases/v$Version.md"),'--outputDir',$packOutput)
    $null = Run-Tool $dotnet $packArgs (Join-Path $work 'pack.log')
    # Publish the same VSIX that is embedded in the desktop plugin manager.
    $pluginVersion = (Get-Content -LiteralPath (Join-Path $root 'extensions/sharpvoice-vscode/package.json') -Raw | ConvertFrom-Json).version
    Copy-Item -LiteralPath (Join-Path $root 'src/SharpVoice.Platform.Windows/Assets/sharpvoice-vscode.vsix') -Destination (Join-Path $packOutput "sharpvoice-vscode-$pluginVersion-win32-x64.vsix")
    $assets = @(Get-ChildItem -LiteralPath $packOutput -File)
    if (-not ($assets.Name -match 'Setup\.exe$') -or -not ($assets.Name -match 'Portable\.zip$') -or -not ($assets.Name -contains 'releases.win-x64.json') -or -not ($assets.Name -match '\.nupkg$')) { throw 'Installer or update-feed assets are missing.' }
    foreach ($asset in $assets) { Copy-Item -LiteralPath $asset.FullName -Destination $output -Force }
    $hashes = $assets | Sort-Object Name | ForEach-Object { ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name) }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$hashes,[Text.Encoding]::ASCII)
    Write-Host "Release packages: $output"
    Write-Host "Build logs: $work"
    Write-Host 'Unsigned build: no publisher code-signing certificate is configured. No release was published.'
}
finally { Pop-Location }
