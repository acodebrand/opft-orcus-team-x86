param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $projectRoot
function Assert-ProjectPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside project: $resolved" }
    return $resolved
}
if (-not $SkipTests) {
    dotnet run --project tests/Orcus.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Simulated core tests failed.' }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$staging = Assert-ProjectPath (Join-Path $projectRoot "artifacts\staging\$stamp\OPTF-ORCUS-TEAM-1.5")
$appOutput = Assert-ProjectPath (Join-Path $staging 'app')
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null
dotnet publish src/Orcus.App/Orcus.App.csproj -c Release -r win-x86 --self-contained true -o $appOutput -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; existing release was preserved.' }
$published = @(Get-ChildItem -LiteralPath $appOutput -File)
if ($published.Count -ne 1 -or $published[0].Name -ne 'OPTF.OrcusTeam.exe') { throw 'Expected one self-contained application file. Existing release was preserved.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $staging 'docs\README.md') -Force
Copy-Item -LiteralPath (Join-Path $appOutput 'OPTF.OrcusTeam.exe') -Destination (Join-Path $projectRoot 'src\Orcus.Installer\Payload\OPTF.OrcusTeam.exe') -Force
$installerOutput = Assert-ProjectPath (Join-Path $projectRoot "artifacts\staging\$stamp\installer")
dotnet publish src/Orcus.Installer/Orcus.Installer.csproj -c Release -r win-x86 --self-contained true -o $installerOutput -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed; existing release was preserved.' }
$installerFiles = @(Get-ChildItem -LiteralPath $installerOutput -File)
if ($installerFiles.Count -ne 1 -or $installerFiles[0].Name -ne 'OPTF-OrcusTeam-Setup.exe') { throw 'Expected one installer EXE.' }
@'
@echo off
cd /d "%~dp0"
start "" "app\OPTF.OrcusTeam.exe"
'@ | Set-Content -LiteralPath (Join-Path $staging 'Iniciar.cmd') -Encoding ascii
$releaseRoot = Assert-ProjectPath (Join-Path $projectRoot 'artifacts\release')
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$destination = Assert-ProjectPath (Join-Path $releaseRoot 'OPTF-ORCUS-TEAM-1.5')
if (Test-Path -LiteralPath $destination) {
    $archive = Assert-ProjectPath (Join-Path $projectRoot "artifacts\archive\release-$stamp")
    New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
    # Both absolute paths have been verified inside this project; preserve the old release.
    Move-Item -LiteralPath $destination -Destination $archive
}
Move-Item -LiteralPath $staging -Destination $destination
Copy-Item -LiteralPath (Join-Path $destination 'app\OPTF.OrcusTeam.exe') -Destination (Join-Path $releaseRoot 'OPTF-OrcusTeam-Portable.exe') -Force
Copy-Item -LiteralPath (Join-Path $installerOutput 'OPTF-OrcusTeam-Setup.exe') -Destination (Join-Path $releaseRoot 'OPTF-OrcusTeam-Setup.exe') -Force
$zipPath = Assert-ProjectPath (Join-Path $releaseRoot 'OPTF-ORCUS-TEAM-win-x86.zip')
Compress-Archive -LiteralPath $destination -DestinationPath $zipPath -Force
& (Join-Path $PSScriptRoot 'package-source.ps1')
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Format-List
Write-Output "Portable application: $destination"
