# Removes only this utility's explicitly named files and startup shortcut.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$installDirectory = [IO.Path]::GetFullPath((Join-Path $localAppData 'YouTubeLinkCleaner'))
$installedExe = Join-Path $installDirectory 'YouTubeLinkCleaner.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'YouTube Link Cleaner.lnk'

# EXE installations own their Windows Apps entry and Start menu shortcuts.
$nativeUninstaller = Join-Path $installDirectory 'Uninstall.exe'
if (Test-Path -LiteralPath $nativeUninstaller -PathType Leaf) {
    Start-Process -FilePath $nativeUninstaller -WindowStyle Hidden
    return
}

function Get-OwnedProcesses {
    @(Get-Process -Name YouTubeLinkCleaner -ErrorAction SilentlyContinue | Where-Object {
        try { [string]::Equals($_.Path, $installedExe, [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    })
}

if (Test-Path -LiteralPath $installDirectory) {
    $item = Get-Item -LiteralPath $installDirectory
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove files from a file or redirected directory: $installDirectory"
    }
}
if ((Get-OwnedProcesses).Count -gt 0) {
    Start-Process -FilePath $installedExe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ((Get-OwnedProcesses).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if ((Get-OwnedProcesses).Count -gt 0) {
        throw 'The cleaner did not exit. Exit it from its tray menu, then run this uninstaller again.'
    }
}

if (Test-Path -LiteralPath $startupLink -PathType Leaf) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupLink)
    if ([string]::Equals($shortcut.TargetPath, $installedExe, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $startupLink -Force
    } else {
        Write-Warning 'The startup shortcut points to another program and was left in place.'
    }
}
foreach ($name in @('YouTubeLinkCleaner.exe', 'README.md', 'Uninstall.ps1')) {
    $target = [IO.Path]::GetFullPath((Join-Path $installDirectory $name))
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($target), $installDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'An uninstall target escaped the installation directory.'
    }
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Remove-Item -LiteralPath $target -Force
    }
}
if ((Test-Path -LiteralPath $installDirectory -PathType Container) -and
    @(Get-ChildItem -LiteralPath $installDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $installDirectory
}
Write-Output 'YouTube Link Cleaner was removed. The extracted package and your clipboard were left in place.'
