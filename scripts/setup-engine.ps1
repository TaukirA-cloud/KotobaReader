param(
    [string]$Python = 'python',
    [ValidateSet('ja','zh','ko','en','es','fr','de')][string]$Source = 'ja',
    [ValidateSet('ja','zh','ko','en','es','fr','de')][string]$Target = 'en',
    [switch]$SkipModel,
    [switch]$InstallTesseract,
    [switch]$SkipOcr
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $projectRoot 'runtime/engine'
if (!(Test-Path (Join-Path $environmentPath 'Scripts/python.exe'))) {
    & $Python -m venv $environmentPath
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.11–3.13 x64 is required.' }
}
$enginePython = Join-Path $environmentPath 'Scripts/python.exe'
& $enginePython -m pip install --only-binary=:all: -r (Join-Path $projectRoot 'worker/requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Engine dependency installation failed.' }
$modelDirectory = Join-Path $projectRoot 'models'
if (!$SkipModel -and !(Test-Path (Join-Path $modelDirectory "${Source}_${Target}"))) {
    & $enginePython (Join-Path $projectRoot 'worker/install_model.py') --source $Source --target $Target --destination $modelDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Model installation failed.' }
}
if (!$SkipOcr) { & (Join-Path $PSScriptRoot 'setup-ocr.ps1') -Source $Source -InstallTesseract:$InstallTesseract }
Write-Host 'Translation engine ready. Open KotobaReader and click Check engine. See docs/SETUP.md for details.'
