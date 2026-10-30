# Installs only for the current Windows user. No administrator rights required.
[CmdletBinding()]
param([switch]$NoStart, [switch]$NoStartup)

$ErrorActionPreference = 'Stop'
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$installDirectory = [IO.Path]::GetFullPath((Join-Path $localAppData 'YouTubeLinkCleaner'))
$installedExe = Join-Path $installDirectory 'YouTubeLinkCleaner.exe'
$sourceExe = Join-Path $PSScriptRoot 'YouTubeLinkCleaner.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'YouTube Link Cleaner.lnk'

function Get-OwnedProcesses {
    @(Get-Process -Name YouTubeLinkCleaner -ErrorAction SilentlyContinue | Where-Object {
        try { [string]::Equals($_.Path, $installedExe, [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    })
}

if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw 'YouTubeLinkCleaner.exe is missing. Keep the extracted package together, or run Build.ps1 first.'
}
if (Test-Path -LiteralPath $installDirectory) {
    $item = Get-Item -LiteralPath $installDirectory
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to install into a file or redirected directory: $installDirectory"
    }
}

$shell = New-Object -ComObject WScript.Shell
if (-not $NoStartup -and (Test-Path -LiteralPath $startupLink)) {
    $existingLink = $shell.CreateShortcut($startupLink)
    if (-not [string]::Equals($existingLink.TargetPath, $installedExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A different program owns this startup shortcut: $startupLink"
    }
}

if ((Get-OwnedProcesses).Count -gt 0) {
    Start-Process -FilePath $installedExe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ((Get-OwnedProcesses).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if ((Get-OwnedProcesses).Count -gt 0) {
        throw 'The existing cleaner did not exit. Exit it from its tray menu, then run the installer again.'
    }
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
if (-not [string]::Equals([IO.Path]::GetFullPath($sourceExe), $installedExe, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
}
foreach ($name in @('README.md', 'Uninstall.ps1')) {
    $source = Join-Path $PSScriptRoot $name
    $destination = Join-Path $installDirectory $name
    if ((Test-Path -LiteralPath $source -PathType Leaf) -and
        -not [string]::Equals([IO.Path]::GetFullPath($source), $destination, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

if (-not $NoStartup) {
    $shortcut = $shell.CreateShortcut($startupLink)
    $shortcut.TargetPath = $installedExe
    $shortcut.Arguments = '--quiet'
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = 'Remove si parameters from copied YouTube links'
    $shortcut.IconLocation = "$installedExe,0"
    $shortcut.Save()
}
if (-not $NoStart) {
    Start-Process -FilePath $installedExe -ArgumentList '--quiet' -WorkingDirectory $installDirectory -WindowStyle Hidden
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ([DateTime]::UtcNow -lt $deadline) {
        $probe = Start-Process -FilePath $installedExe -ArgumentList '--status' -WindowStyle Hidden -PassThru
        try {
            $remainingMilliseconds = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
            if (-not $probe.WaitForExit($remainingMilliseconds)) { break }
            if ($probe.ExitCode -eq 0 -and (Get-OwnedProcesses).Count -gt 0) {
                $ready = $true
                break
            }
        } finally {
            $probe.Dispose()
        }
        if ([DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $ready) {
        throw "Files were installed, but the cleaner did not confirm it is running. Launch '$installedExe' to inspect the problem."
    }
}
Write-Output "Installed: $installedExe"
if (-not $NoStartup) { Write-Output 'Starts automatically at Windows sign-in.' }
if (-not $NoStart) { Write-Output 'Running in the notification area. Right-click its tray icon for controls.' }
