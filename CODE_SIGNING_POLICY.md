# Code signing policy

## Current status

The project is preparing an application to the SignPath Foundation's free
open-source code-signing program. Approval has not been granted and signing is
not configured. The current v1.1.0 executables are **unsigned**.

If approved and configured, signed releases will carry this attribution:

> Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

This attribution describes the planned signing arrangement, not the current
unsigned downloads. Future release notes will explicitly identify signed builds.

## Team and responsibilities

- Committer and reviewer: [ThePaint3DHackerman](https://github.com/ThePaint3DHackerman).
- Release and signing approver: [ThePaint3DHackerman](https://github.com/ThePaint3DHackerman).

Contributions from other authors must be reviewed by the maintainer before
release. Signing requests must be approved by the release approver. Before
signing is enabled, all participating accounts must use multi-factor
authentication as required by SignPath.

## Artifacts eligible for signing

Only official app, installer, and uninstaller executables built from this
repository's published source and build scripts are eligible. Release artifacts
must be traceable to a published source commit or tag. Third-party binaries and
unreviewed contributions must not be signed under this project's identity.
Signing credentials must never be committed to the repository.

## Privacy policy

The clipboard utility processes clipboard text locally. It sends no network
requests, collects no telemetry, and does not store clipboard text, history, or
logs. The installer and uninstaller do not send network requests.

X links are rewritten to FixupX. Opening those links or having another app
fetch a preview contacts that external service; the clipboard utility does not
make those requests. See the [FxEmbed/FixupX project](https://github.com/FxEmbed/FxEmbed)
for information about that service. Windows clipboard history, synchronization,
and other programs operate independently of this utility.

See [Behavior and privacy](README.md#behavior-and-privacy) for supported links,
clipboard handling, and the limits of link sanitization.