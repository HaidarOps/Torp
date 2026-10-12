param([string]$Executable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $projectRoot 'dist\Torp-1.0.0\Torp.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Package the app before running the executable smoke test.' }
$smokeRoot = Join-Path $projectRoot ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMddHHmmss'))
$vaultRoot = Join-Path $smokeRoot 'vault'
$settingsFile = Join-Path $smokeRoot 'settings.xml'
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
$launchArguments = @('--vault', ('"' + $vaultRoot + '"'), '--settings', ('"' + $settingsFile + '"'), '--smoke-test')
$appProcess = Start-Process -FilePath $Executable -ArgumentList $launchArguments -WindowStyle Hidden -PassThru
try {
    if (-not $appProcess.WaitForExit(15000)) { throw 'Torp did not finish startup verification and normal shutdown within 15 seconds.' }
    if ($appProcess.ExitCode -ne 0) { throw "Torp startup verification failed with code $($appProcess.ExitCode)." }
    if (-not (Test-Path -LiteralPath (Join-Path $vaultRoot 'Welcome to Torp.md'))) { throw 'Starter note was not created.' }
    if (-not (Test-Path -LiteralPath $settingsFile)) { throw 'Preferences were not saved on normal exit.' }
    $resultText = 'PASS packaged executable: main window, editor, preview, index, starter vault, normal close, preferences, exit code 0'
    Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\smoke-results.txt') -Value $resultText -Encoding UTF8
    Write-Host $resultText
} finally {
    if (-not $appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
    $appProcess.Dispose()
}
