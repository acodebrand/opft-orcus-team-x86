$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $projectRoot
# These tests never execute the operational scripts or the real Windows backend.
dotnet run --project tests/Orcus.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core simulation failed.' }
dotnet build src/Orcus.App/Orcus.App.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
foreach ($script in Get-ChildItem -LiteralPath 'src\Orcus.App\Services' -Filter '*.ps1') {
    $tokens = $null; $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
}
$sim = Start-Process -FilePath '.\src\Orcus.App\bin\Release\net10.0-windows\OPTF.OrcusTeam.exe' -ArgumentList '--ui-test','artifacts/qa-simulated' -PassThru -WindowStyle Hidden
if (-not $sim.WaitForExit(45000)) { $sim.Kill(); throw 'The simulated UI test timed out.' }
$sim.Refresh()
$report = Get-Content -LiteralPath 'artifacts\qa-simulated\ui-results.txt' -Raw
Write-Output $report
if ($sim.ExitCode -ne 0 -or $report.Contains('FAIL')) { throw 'UI simulation failed.' }
