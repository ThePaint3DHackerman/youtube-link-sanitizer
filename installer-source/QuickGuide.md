# YouTube Link Cleaner

Run YouTubeLinkCleaner-Setup.exe, then choose Install and run. This installs for your Windows account and can start automatically when you sign in. No administrator access or downloads are needed. Requires Windows 10/11 with .NET Framework 4.8.

Copy YouTube or X links and paste normally in Discord or other apps. YouTube links lose only the si share parameter; x.com and www.x.com links become fixupx.com for embeds. Timestamps, playlists and surrounding text stay intact.

Before: https://youtu.be/dQw4w9WgXcQ?si=share-token&t=69
After:  https://youtu.be/dQw4w9WgXcQ?t=69

Before: https://x.com/coolporyg0n/status/2116327687707123755
After:  https://fixupx.com/coolporyg0n/status/2116327687707123755

For X links, only the hostname changes; the path, query and fragment stay intact. Already rewritten links and lookalike domains are left alone. FixupX is an external service used when a rewritten link is opened or previewed; embed availability depends on that service and the post.

Right-click the Y icon in your Windows tray (possibly behind the ^ arrow) to pause, resume, clean now or exit. Ctrl+Alt+V checks the clipboard immediately before pasting. Clipboard notifications are asynchronous, so an extremely fast normal paste can beat the cleaner. The shortcut can be unavailable if another app uses it, and Windows can block it in elevated apps. Tray status reports availability.

The app runs locally, makes no network requests and saves no clipboard content or logs. It changes copied text only when a YouTube si parameter needs removal or an X hostname needs rewriting. Changed rich text becomes plain text so stale HTML links cannot survive. Files, images, excluded sensitive items and text over 1 MiB are skipped.

Removing this URL parameter does not provide anonymity or stop cookies, account activity or other forms of tracking. Existing clipboard history entries are not erased.

To remove it, use Windows Settings > Apps, or Start > YouTube Link Cleaner > Uninstall YouTube Link Cleaner. No reboot is needed. Your clipboard and unrelated files are left alone.

The standalone installer contains everything. You can give that one EXE to friends; they do not need a source folder or PowerShell commands. This build is unsigned, so Windows may show an unknown-publisher warning.
