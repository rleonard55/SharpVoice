$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw 'The native build requires Visual Studio C++ build tools.' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
    if (-not (Test-Path $vcvars)) { throw 'Visual Studio x64 C++ tools were not found.' }
    $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpvoice-diagnostic-build-' + [guid]::NewGuid().ToString('N') + '.cmd')
    try {
        $script = @"
@echo off
call "$vcvars"
if errorlevel 1 exit /b 1
dotnet publish src/SharpVoice.Diagnostics/SharpVoice.Diagnostics.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:UseSharedCompilation=false -p:IlcUseEnvironmentalTools=true -m:1 -o artifacts/diagnostics --nologo
exit /b %errorlevel%
"@
        [System.IO.File]::WriteAllText($temporary, $script)
        & $env:ComSpec /d /c $temporary
        if ($LASTEXITCODE -ne 0) { throw 'Wake Diagnostics publication failed.' }
    }
    finally { if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    Write-Host 'Ready: Launch-Wake-Diagnostics.cmd'
}
finally { Pop-Location }
