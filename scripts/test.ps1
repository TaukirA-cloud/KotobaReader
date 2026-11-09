param([switch]$Ui, [switch]$Integration, [string]$Dotnet = 'dotnet', [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $Dotnet build src/KotobaReader/KotobaReader.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $Dotnet run --project tests/KotobaReader.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core regression checks failed.' }
    & $Python -m unittest discover -s tests -p 'test_*.py' -v
    if ($LASTEXITCODE -ne 0) { throw 'Worker regression checks failed.' }
    if ($Ui -or $Integration) {
        $env:KOTOBA_INTEGRATION = if ($Integration) { '1' } else { '0' }
        & $Dotnet run --project tests/KotobaReader.Smoke -c Release -- artifacts/smoke
        if ($LASTEXITCODE -ne 0) { throw 'Windows UI smoke checks failed.' }
    }
} finally { Remove-Item Env:KOTOBA_INTEGRATION -ErrorAction SilentlyContinue; Pop-Location }
