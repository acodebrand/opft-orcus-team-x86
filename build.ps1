param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'scripts\build.ps1') -SkipTests:$SkipTests
