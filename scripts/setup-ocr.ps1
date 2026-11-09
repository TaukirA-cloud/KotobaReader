param(
    [ValidateSet('ja','zh','ko','en','es','fr','de')][string]$Source = 'ja',
    [switch]$InstallTesseract
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($InstallTesseract) {
    & winget install --id UB-Mannheim.TesseractOCR --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) { throw 'Tesseract installation failed. See docs/SETUP.md for manual installation.' }
}
$languageCodes = @{ ja='jpn'; zh='chi_sim'; ko='kor'; en='eng'; es='spa'; fr='fra'; de='deu' }
$languages = @('eng', $languageCodes[$Source])
if ($Source -eq 'ja') { $languages += 'jpn_vert' }
$dataPath = Join-Path $projectRoot 'runtime/tessdata'
New-Item -ItemType Directory -Force $dataPath | Out-Null
$provenance = @()
foreach ($language in ($languages | Select-Object -Unique)) {
    $destination = Join-Path $dataPath "$language.traineddata"
    $url = "https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/$language.traineddata"
    if (!(Test-Path $destination)) {
        Write-Host "Downloading $language OCR data..."
        $temporary = "$destination.tmp"
        Invoke-WebRequest -Uri $url -OutFile $temporary
        Move-Item -LiteralPath $temporary -Destination $destination
    }
    $provenance += @{ language=$language; url=$url; sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
}
$provenance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dataPath "provenance-$Source.json") -Encoding UTF8
Write-Host "OCR data ready: $dataPath"
