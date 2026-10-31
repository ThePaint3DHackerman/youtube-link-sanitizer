# YouTube Link Sanitizer

A small Windows notification-area utility that removes the `si` share parameter
from YouTube links and rewrites X links to FixupX for Discord embeds.
The repository is named
`youtube-link-sanitizer`; the existing application and executable names remain
**YouTube Link Cleaner** and `YouTubeLinkCleaner.exe`.

```text
Before: https://youtu.be/dQw4w9WgXcQ?si=share-token&t=69
After:  https://youtu.be/dQw4w9WgXcQ?t=69

Before: https://x.com/coolporyg0n/status/2116327687707123755
After:  https://fixupx.com/coolporyg0n/status/2116327687707123755
```

## Download for Windows

- **[Download the installer](https://github.com/ThePaint3DHackerman/youtube-link-sanitizer/releases/latest/download/YouTubeLinkCleaner-Setup.exe)** — run it and choose **Install and run**. Startup at sign-in is optional.
- **[Download the portable ZIP](https://github.com/ThePaint3DHackerman/youtube-link-sanitizer/releases/latest/download/YouTubeLinkCleaner-Portable.zip)** — extract the folder and run `YouTubeLinkCleaner.exe`.
- [All releases, matching source code, and SHA-256 checksums](https://github.com/ThePaint3DHackerman/youtube-link-sanitizer/releases/latest).

Requires Windows 10 or 11 with .NET Framework 4.8. No Lua, Python, compiler, or
build tools are needed to use these downloads. The executables are unsigned, so
Windows may display an unknown-publisher warning.

Prebuilt packages are attached to GitHub Releases. Generated binaries stay out
of the Git source history.

## Requirements for building from source

Windows PowerShell 5.1 and the .NET Framework C# compiler are required for
building. The build script checks Windows' 64-bit Framework compiler and falls
back to the 32-bit location. No Python, Visual Studio, NuGet, or downloaded build
packages are used.

## Build and test

Open PowerShell in the repository folder and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1
```

The first command produces `YouTubeLinkCleaner.exe` in the repository root. The
second builds and runs the parser regression suite, without using the clipboard.
`-ExecutionPolicy Bypass` applies to that PowerShell process.

Optional native clipboard integration tests are separate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1 -Integration
```

They require creation of a private Windows window station and stop if Windows
denies it. Inspect the test code before running native integration tests.

To build the standalone installer after building the app:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer-source\BuildSetup.ps1
```

This produces `YouTubeLinkCleaner-Setup.exe`, generates a payload hash source
file and uninstaller, and runs the setup executable in its payload-verification
mode. It does not install the application in that mode. The setup source, icon,
embedded quick guide, and installer regression sources are in `installer-source/`.
Installer regression sources are not run by `Test.ps1`.

## Run and use

Run the newly built `YouTubeLinkCleaner.exe` to use it without installing. Close
it through its notification-area menu. Right-click its tray icon (possibly under
the Windows `^` overflow arrow) to pause/resume, clean now, view status, or exit.

Copy a YouTube or X link and paste normally into Discord or another desktop app.
**Ctrl+Alt+V** requests cleaning immediately before pasting into the active app.
Windows clipboard notifications are asynchronous: an extremely fast ordinary
paste can happen before cleaning. Another clipboard writer can still interfere.
Windows may block simulated paste into elevated apps, and the shortcut can be
unavailable if another app already uses it. The tray status reports availability.

Command-line options are `--quiet` (suppress the initial notice), `--exit`
(request exit), and `--status` (exit code 0 when a running instance responds,
1 when absent). Only one instance runs per Windows session.

## Optional installation

Run the built `YouTubeLinkCleaner-Setup.exe` and choose **Install and run**. The
installer works for the current Windows account, offers startup at sign-in, and
registers an uninstall entry in Windows Settings > Apps. Administrator access
is not required. Locally built executables are unsigned.

Alternatively, after building the app, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

That script installs under `%LOCALAPPDATA%\YouTubeLinkCleaner`, starts the app,
and creates a startup shortcut. Use `-NoStart` and/or `-NoStartup` to omit those
steps. Installation and running the app affect your Windows account and live
clipboard; building and the default parser tests do not.

For an EXE installation, uninstall through Windows Settings > Apps or its Start
menu uninstall shortcut. For a PowerShell installation, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

## Behavior and privacy

- Only `si` query parameters on supported YouTube hosts are removed. Timestamps,
  video IDs, playlists, other parameters, fragments, and surrounding text are
  retained. Ambiguous inputs can be left unchanged.
- The exact hosts `x.com` and `www.x.com` become `fixupx.com`. HTTP(S) and bare
  links are supported; paths, query strings, fragments, and surrounding text are
  preserved. Already rewritten links, other subdomains, unrelated sites and
  lookalike domains are left alone. Ambiguous adjacent URLs can be left unchanged.
- FixupX is an external embed service. Opening a rewritten link or generating a
  preview uses that service; the clipboard utility itself performs no requests.
  Embed availability depends on the service and post. See the
  [FxEmbed/FixupX project](https://github.com/FxEmbed/FxEmbed).
- A cleaned or rewritten item becomes plain text, removing rich HTML/RTF that
  could contain a stale hidden URL. Other text, file and image items remain
  unchanged.
- Text beyond the 1 MiB limit and items marked as sensitive or excluded from
  monitoring are skipped.
- The application sends no network requests and does not store clipboard text,
  keep history, or write clipboard logs. It cannot erase an original link already
  captured by Windows history, synchronization, or another program.
- Removing a URL parameter does not provide anonymity or stop cookies, signed-in
  activity, IP-based tracking, or all ways of linking accounts.

## Source layout

- `source/`: clipboard utility and URL parser.
- `tests/`: parser and native clipboard regression sources.
- `installer-source/`: standalone setup/uninstaller sources and quick guide.
- `Build.ps1`, `Test.ps1`: app build and parser tests.
- `Install.ps1`, `Uninstall.ps1`: optional current-user PowerShell installation.

## License

The original application code is licensed under **GPL-3.0-only**. See [LICENSE](LICENSE).
Third-party rights and terms remain separate; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
