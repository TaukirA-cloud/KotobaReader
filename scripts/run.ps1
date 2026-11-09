param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools/dotnet10/dotnet.exe'
if (!(Test-Path $localDotnet)) { $localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe' }
$dotnetCommand = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
if (Test-Path $localDotnet) { $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli' }
$project = Join-Path $projectRoot 'src/KotobaReader/KotobaReader.csproj'
if ($Demo) { & $dotnetCommand run --project $project -c Release -- --demo }
else { & $dotnetCommand run --project $project -c Release }
exit $LASTEXITCODE
