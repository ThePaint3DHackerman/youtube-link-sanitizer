[CmdletBinding()]
param([switch]$Integration)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$testDirectory = Join-Path $PSScriptRoot 'tests'
$sourceDirectory = Join-Path $PSScriptRoot 'source'
$testExe = Join-Path $testDirectory 'SanitizerTests.exe'
& $compiler /nologo /target:exe /warnaserror+ "/out:$testExe" (Join-Path $sourceDirectory 'YouTubeSanitizer.cs') (Join-Path $testDirectory 'SanitizerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sanitizer test compilation failed.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Sanitizer tests failed.' }

if ($Integration) {
    $integrationExe = Join-Path $testDirectory 'ClipboardIntegrationTests.exe'
    $sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:exe /warnaserror+ /main:Barnacle.ClipboardIntegrationTests /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$integrationExe" @sources (Join-Path $testDirectory 'ClipboardIntegrationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard integration test compilation failed.' }
    & $integrationExe
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard integration tests could not pass. A private Windows window station is required; no fallback to the live clipboard is attempted.' }
}
