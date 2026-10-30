[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$app = Join-Path $PSScriptRoot 'YouTubeLinkCleaner.exe'
if (-not (Test-Path -LiteralPath $app)) { $app = Join-Path (Split-Path -Parent $PSScriptRoot) 'YouTubeLinkCleaner.exe' }
$engine = Join-Path $PSScriptRoot 'InstallerEngine.cs'
$uninstaller = Join-Path $PSScriptRoot 'Uninstall.exe'
$guide = Join-Path $PSScriptRoot 'QuickGuide.md'
$icon = Join-Path $PSScriptRoot 'App.ico'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$common = @('/nologo', '/warnaserror+', '/target:winexe', '/optimize+', '/platform:anycpu', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', "/win32manifest:$manifest", "/win32icon:$icon")
& $compiler @common "/out:$uninstaller" $engine (Join-Path $PSScriptRoot 'Uninstall.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }
$hashSource = 'namespace BarnacleSetup { internal static class PayloadHashes { internal const string App = "' + (Get-FileHash -LiteralPath $app).Hash + '"; internal const string Uninstaller = "' + (Get-FileHash -LiteralPath $uninstaller).Hash + '"; internal const string Readme = "' + (Get-FileHash -LiteralPath $guide).Hash + '"; } }'
$hashFile = Join-Path $PSScriptRoot 'PayloadHashes.cs'
[IO.File]::WriteAllText($hashFile, $hashSource, [Text.Encoding]::UTF8)
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'YouTubeLinkCleaner-Setup.exe'
& $compiler @common "/out:$output" "/resource:$app,payload.app" "/resource:$uninstaller,payload.uninstaller" "/resource:$guide,payload.readme" "/resource:$icon,payload.icon" $engine (Join-Path $PSScriptRoot 'Setup.cs') $hashFile
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
$verification = Start-Process -FilePath $output -ArgumentList '--verify' -WindowStyle Hidden -PassThru -Wait
if ($verification.ExitCode -ne 0) { throw 'Embedded payload verification failed.' }
Write-Output "Built standalone installer: $output"
