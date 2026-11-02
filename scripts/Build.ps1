param([switch]$Aot)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if ($Aot) { Remove-Item 'artifacts/aot-update/update.ready' -ErrorAction SilentlyContinue }
    dotnet run --project tests/SharpVoice.Tests/SharpVoice.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Prototype checks failed.' }
    $aotValue = if ($Aot) { 'true' } else { 'false' }
    $output = if ($Aot) { 'artifacts/aot-update' } else { 'artifacts/prototype' }
    if ($Aot) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio C++ build tools and the Windows SDK first.' }
        $vsInstall = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if (-not $vsInstall) { throw 'Visual Studio x64 C++ build tools were not found.' }
        $vcvars = Join-Path $vsInstall 'VC/Auxiliary/Build/vcvars64.bat'
        New-Item -ItemType Directory -Path '.build-cache' -Force | Out-Null
        $batch = @"
@echo off
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "$vcvars"
if errorlevel 1 exit /b 1
dotnet publish src/SharpVoice.App/SharpVoice.App.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:IlcUseEnvironmentalTools=true -o artifacts/aot-update
exit /b %errorlevel%
"@
        Set-Content -LiteralPath '.build-cache/publish-windows-aot.cmd' -Value $batch -Encoding ASCII
        & $env:ComSpec /d /c '.build-cache\publish-windows-aot.cmd'
    }
    else {
        dotnet publish src/SharpVoice.App/SharpVoice.App.csproj -c Release -r win-x64 --self-contained true "-p:PublishAot=$aotValue" -o $output
    }
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Aot) {
        New-Item -ItemType Directory -Path 'artifacts/portable' -Force | Out-Null
        Copy-Item "$output/SharpVoice.exe" 'artifacts/portable/SharpVoice.exe' -Force
        Set-Content -LiteralPath "$output/update.ready" -Value 'Verified build ready for launcher installation.' -Encoding ASCII
    }
    Write-Host "Published: $output/SharpVoice.exe"
}
finally { Pop-Location }
