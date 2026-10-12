param([switch]$StrictNet40)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Test -StrictNet40:$StrictNet40
if (-not $?) { throw 'Build or tests failed; package was not produced.' }
$distRoot = Join-Path $projectRoot 'dist'
$packageRoot = Join-Path $distRoot 'Torp-1.0.0'
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
foreach ($name in @('Torp.exe', 'Torp.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\bin\$name") -Destination $packageRoot -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $packageRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\compatibility.txt') -Destination $packageRoot -Force
$archive = Join-Path $distRoot 'Torp-1.0.0-windows-x86.zip'
Compress-Archive -LiteralPath $packageRoot -DestinationPath $archive -Force
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
Set-Content -LiteralPath "$archive.sha256" -Value ($hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($archive)) -Encoding ASCII
Write-Host "Packaged $archive"
