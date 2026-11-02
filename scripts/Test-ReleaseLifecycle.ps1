param([string]$ReleaseDirectory = 'artifacts/releases/0.1.2')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
$packId = 'SharpVoice.ReleaseTest.' + [Guid]::NewGuid().ToString('N')
$work = Join-Path $root ('.build-cache/' + $packId)
$install = Join-Path $work 'installed'
New-Item -ItemType Directory -Path $work -Force | Out-Null
function Q([string]$value) { '"' + [regex]::Replace([regex]::Replace($value,'(\\*)"','$1$1\"'),'(\\+)$','$1$1') + '"' }
function Run([string]$file,[string[]]$parameters,[string]$name,[int]$expected=0) {
    $p = [Diagnostics.Process]::new(); $p.StartInfo.FileName = $file
    $p.StartInfo.Arguments = (($parameters | ForEach-Object { Q $_ }) -join ' ')
    $p.StartInfo.WorkingDirectory=$root; $p.StartInfo.UseShellExecute=$false
    $p.StartInfo.RedirectStandardOutput=$true; $p.StartInfo.RedirectStandardError=$true
    $p.StartInfo.EnvironmentVariables['PATHEXT']='.COM;.EXE;.BAT;.CMD'
    $null=$p.Start();$a=$p.StandardOutput.ReadToEndAsync();$b=$p.StandardError.ReadToEndAsync();$p.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $work ($name+'.log')),$a.Result+$b.Result)
    if($p.ExitCode -ne $expected){throw "$name failed with exit $($p.ExitCode). See $work"}
    return $a.Result
}
function Read-Report([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
Push-Location $root
try {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs=(Run $vswhere @('-latest','-products','*','-requires','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','-property','installationPath') 'vswhere').Trim()
    if(-not $vs){throw 'Visual Studio x64 C++ tools are required.'}
    $vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
    foreach($version in @('0.1.0','0.1.1')) {
        $publish=Join-Path $work ('publish-'+$version)
        $argsForPublish=@('publish','tests/SharpVoice.ReleaseFixture/SharpVoice.ReleaseFixture.csproj','-c','Release','--self-contained','true',"-p:ReleaseFixtureVersion=$version",'-p:DebugType=None','-p:DebugSymbols=false','-p:NativeDebugSymbols=false','-p:CopyOutputSymbolsToPublishDirectory=false','-p:StripSymbols=true','-p:UseSharedCompilation=false','-p:IlcUseEnvironmentalTools=true','-p:IlcTreatWarningsAsErrors=true',"-p:RestorePackagesPath=$root/.build-cache/nuget",'--artifacts-path',"$work/build",'-o',$publish,'--nologo')
        $command=(Q $dotnet)+' '+(($argsForPublish|ForEach-Object {Q $_}) -join ' ')
        $batch=Join-Path $work ('publish-'+$version+'.cmd')
        [IO.File]::WriteAllText($batch,"@echo off`r`nset `"PATHEXT=.COM;.EXE;.BAT;.CMD`"`r`ncall `"$vcvars`"`r`nif errorlevel 1 exit /b 1`r`n$command`r`nexit /b %errorlevel%`r`n",[Text.Encoding]::ASCII)
        Run $env:ComSpec @('/d','/c',$batch) ('publish-'+$version)
        Run $dotnet @('tool','run','vpk','--','pack','--packId',$packId,'--packVersion',$version,'--packDir',$publish,'--mainExe','SharpVoice.ReleaseFixture.exe','--runtime','win-x64','--channel','win-x64','--shortcuts','None','--delta','None','--outputDir',(Join-Path $work ('feed-'+$version))) ('pack-'+$version)
    }
    $setup=(Get-ChildItem (Join-Path $work 'feed-0.1.0') -Filter '*Setup.exe').FullName
    Run $setup @('--silent','--installto',$install) 'install'
    $exe=Join-Path $install 'current/SharpVoice.ReleaseFixture.exe'
    if(-not (Test-Path $exe)){throw 'Fixture did not install.'}
    $report=Join-Path $work 'report.json'
    Run $exe @('--report',$report) 'installed-report'
    $state=Read-Report $report
    if($state.version -ne '0.1.0' -or -not $state.installed){throw 'Installed fixture metadata invalid.'}
    Write-Host 'PASS isolated per-user install and bootstrap.'
    $corrupt=Join-Path $work 'corrupt-feed'
    Copy-Item (Join-Path $work 'feed-0.1.1') $corrupt -Recurse
    $package=(Get-ChildItem $corrupt -Filter '*full.nupkg').FullName
    $content=[IO.File]::ReadAllBytes($package);$content[100]=$content[100] -bxor 0x7F;[IO.File]::WriteAllBytes($package,$content)
    Run $exe @('--download-local',$corrupt,$report) 'corrupt-download' 2
    if(-not (Read-Report $report).error_type){throw 'Corrupt update was accepted.'}
    Write-Host 'PASS corrupt package rejected without application.'
    Run $exe @('--apply-local',(Join-Path $work 'feed-0.1.1'),$report) 'apply-update'
    $deadline=[DateTime]::UtcNow.AddSeconds(60)
    do { Start-Sleep -Milliseconds 250; try {$state=Read-Report $report}catch{$state=$null} } while(($null -eq $state -or $state.version -ne '0.1.1') -and [DateTime]::UtcNow -lt $deadline)
    if($state.version -ne '0.1.1' -or -not $state.installed){throw 'Update/restart did not reach 0.1.1.'}
    Write-Host 'PASS verified local-feed update and restart to next version.'
    Run $exe @('--check-local',(Join-Path $work 'feed-0.1.0'),$report) 'no-downgrade'
    if((Read-Report $report).candidate){throw 'Older release was offered as an update.'}
    Write-Host 'PASS downgrade not offered.'
    Run (Join-Path $install 'Update.exe') @('uninstall','--silent') 'uninstall'
    if(Test-Path $exe){throw 'Fixture uninstall did not remove its application.'}
    Write-Host 'PASS isolated uninstall; no SharpVoice preferences, microphone or running app used.'
    # Test the real release ZIP without installing it or opening the microphone.
    $portable=Get-ChildItem (Join-Path $root $ReleaseDirectory) -Filter '*Portable.zip' | Select-Object -First 1
    if($portable) {
        $unpacked=Join-Path $work 'real-portable';Expand-Archive -LiteralPath $portable.FullName -DestinationPath $unpacked
        $real=(Get-ChildItem $unpacked -Recurse -Filter SharpVoice.exe | Where-Object {$_.Length -gt 1MB} | Select-Object -First 1).FullName
        Run $real @('--release-info',$report) 'real-portable-info'
        if(-not (Read-Report $report).updates_supported){throw 'Real portable package has no updater integration.'}
        Write-Host 'PASS real native release portable metadata without capture/authentication.'
        Run $real @('--verify-bundled-model',$report) 'real-portable-bundled-model'
        $bundled = Read-Report $report
        if($bundled.model -ne 'tiny.en-q5_1' -or $bundled.network_requests -ne 0 -or $bundled.sha256 -ne 'c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b'){throw 'Portable default model is missing or requires the network.'}
        Write-Host 'PASS real portable Tiny Q5 English resolves offline with its pinned hash.'
    }
    Write-Host "Lifecycle logs: $work"
}
finally {
    # Clean the isolated test installation even after a failed assertion.
    if(Test-Path (Join-Path $install 'Update.exe')) { try { Run (Join-Path $install 'Update.exe') @('uninstall','--silent') 'cleanup-uninstall' } catch { Write-Warning "Test installation remains at $install" } }
    Pop-Location
}
