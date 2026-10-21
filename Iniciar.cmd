@echo off
cd /d "%~dp0"
if exist "artifacts\release\OPTF-OrcusTeam-Portable.exe" (
    start "" "artifacts\release\OPTF-OrcusTeam-Portable.exe"
) else (
    echo Primero compila la aplicacion con build.ps1.
    pause
)
