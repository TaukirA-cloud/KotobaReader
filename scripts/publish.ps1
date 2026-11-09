param(
    [string]$Dotnet = 'dotnet',
    [ValidateSet('win-x64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = if ($OutputDirectory) {
    if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory }
    else { Join-Path $projectRoot $OutputDirectory }
} else { Join-Path $projectRoot "artifacts/KotobaReader-$Runtime" }
# Preserve previous releases and allow publishing repeatedly without manual cleanup.
if ((Test-Path $output) -or (Test-Path "$output.zip")) {
    $output += '-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
}
& $Dotnet publish (Join-Path $projectRoot 'src/KotobaReader/KotobaReader.csproj') -p:PublishProfile=WindowsPortable -r $Runtime -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $output -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts') -Destination $output -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'worker/requirements.txt') -Destination (Join-Path $output 'worker')
Compress-Archive -Path (Join-Path $output '*') -DestinationPath "$output.zip"
$checksum = (Get-FileHash -LiteralPath "$output.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $([System.IO.Path]::GetFileName($output)).zip" | Set-Content -LiteralPath "$output.zip.sha256" -Encoding ASCII
Write-Host "Created $output.zip. Reader runtime included; Python, OCR, and translation model are installed separately."
Write-Host "Launch: $(Join-Path $output 'KotobaReader.exe')"
