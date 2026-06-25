# DeskRealm development journal

## 2026-06-25 — v0.7.0 release freeze

DeskRealm v0.7.0 closes the Realm Studio modernization cycle. The project moved from a WinForms management surface to a WinUI 3 dashboard and unified modal host while preserving the Windows-first core: virtual desktop GUID identity, Desktop Known Folder switching, Shell-backed icon layouts and explicit user-visible recovery behavior.

### Validated Windows discoveries carried into the release

- Explorer Registry desktop-name metadata is persistent, but Task View reloads it only after an Explorer restart or reboot. DeskRealm therefore gives the user an explicit choice rather than pretending the rename is instant.
- Explorer owns the notification area. When it restarts, DeskRealm must re-register its tray icon after the `TaskbarCreated` notification.
- The native/original Desktop realm can be relabeled safely, but the physical Desktop folder must remain untouched.
- Wallpaper state needs bidirectional reconciliation: DeskRealm can apply a selected image, and it can import a readable Windows assignment changed externally.
- Direct realm-card controls need shared runtime services and unified modal orchestration; duplicating hotkey/wallpaper logic per card created avoidable state races.

### Release hardening

The candidate-template/source-repair maze is gone. Release compilation is now ordinary Windows .NET work: restore the publish RID, build Release, publish self-contained `win-x64`, validate the executable and smoke-test it.

The v0.7.0 release workflow builds on `main`, pull requests and tags with read-only permissions. A separate tag-only job creates the GitHub Release and attaches the portable/install-bundle ZIPs plus SHA-256 checksums.

### Next

The project is intentionally not jumping straight into more Shell features. The next milestones are recorded in `docs/ROADMAP.md`: testability/service boundaries first, diagnostics/supportability second, then distribution and compatibility hardening for a v1.0.0 contract.

## 2026-06-25 — Release storytelling and README media alignment

The public v0.7.0 story was corrected to match the actual user value of the release. The README and release notes now lead with Realm Studio as an immediate-control dashboard rather than leading with internal Shell mechanics: each virtual desktop can be switched, previewed, wallpapered, hotkeyed, protected and selected as the default directly from its realm card.

Two current GitHub-hosted demonstration GIFs are documented in the README: the first shows realm switching and layout continuity; the second shows the Realm Studio UI and its direct controls. The changelog release heading is standardized as `v0.7.0` with a public title so the local release helper recognises it deterministically.

## 2026-06-25 — v0.7.0 release helper encoding repair (`_bl`)

- Windows PowerShell 5.1 failed to parse `Prepare-GitHubRelease.ps1` because a raw UTF-8 em dash in a double-quoted error message could be decoded as a smart quote in a no-BOM script.
- The public CHANGELOG remains intentionally typographic (`## v0.7.0 — Realm Studio: ...`). The helper now constructs the separator at runtime with `[char]0x2014`; all executable PowerShell source remains ASCII-only.
- This is a release-tooling repair only; no DeskRealm runtime behavior changed.
