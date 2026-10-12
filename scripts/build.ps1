param(
    [switch]$Test,
    [switch]$StrictNet40
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot 'artifacts'
$binRoot = Join-Path $outputRoot 'bin'
New-Item -ItemType Directory -Force -Path $binRoot | Out-Null
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Install .NET Framework 4.x or supply a Windows build machine with csc.exe.' }
$referenceRoot = $env:TORP_NET40_REFS
if (-not $referenceRoot) { $referenceRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0' }
$hasNet40 = Test-Path -LiteralPath (Join-Path $referenceRoot 'mscorlib.dll')
if ($StrictNet40 -and -not $hasNet40) { throw 'Strict XP build requires .NET 4.0 reference assemblies. Install the targeting pack or set TORP_NET40_REFS.' }
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')
$common = @('/nologo', '/noconfig', '/langversion:4', '/warn:4', '/warnaserror+', '/optimize+', '/platform:x86', '/utf8output')
if ($hasNet40) {
    $common += '/nostdlib+'
    foreach ($reference in $references) { $common += '/reference:' + (Join-Path $referenceRoot $reference) }
    $compatibility = 'Compiled against .NET Framework 4.0 reference assemblies; XP runtime acceptance remains required.'
} else {
    foreach ($reference in $references) { $common += '/reference:' + $reference }
    $compatibility = 'Local build against installed .NET 4.x runtime libraries. XP API compatibility is NOT certified.'
    Write-Warning $compatibility
}
$coreSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\Torp.Core') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$appSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\Torp.App') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$coreDll = Join-Path $binRoot 'Torp.Core.dll'
$appExe = Join-Path $binRoot 'Torp.exe'
& $compilerPath @common /target:library "/out:$coreDll" @coreSources
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
& $compilerPath @common /target:winexe "/out:$appExe" @coreSources @appSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Torp.App\Torp.exe.config') -Destination "$appExe.config" -Force
Set-Content -LiteralPath (Join-Path $outputRoot 'compatibility.txt') -Value $compatibility -Encoding UTF8
Write-Host "Built $appExe"
if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
    if ($testSources.Count -eq 0) { throw 'The test suite is missing.' }
    $testExe = Join-Path $binRoot 'Torp.Tests.exe'
    & $compilerPath @common /target:exe /main:Torp.Tests.TestRunner "/out:$testExe" "/reference:$coreDll" @appSources @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item -LiteralPath "$appExe.config" -Destination "$testExe.config" -Force
    & $testExe (Join-Path $outputRoot 'tests') | Tee-Object -FilePath (Join-Path $outputRoot 'test-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Component tests failed.' }
}
