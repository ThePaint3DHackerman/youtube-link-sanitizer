[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDirectory 'csc.exe'))) {
    $frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDirectory 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler was not found. This package targets .NET Framework 4.8 on Windows 10/11.'
}
$sourceDirectory = Join-Path $PSScriptRoot 'source'
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No C# source files were found in source/.' }
$executable = Join-Path $PSScriptRoot 'YouTubeLinkCleaner.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    "/out:$executable", "/win32manifest:$(Join-Path $sourceDirectory 'app.manifest')") + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Write-Output "Built: $executable"
