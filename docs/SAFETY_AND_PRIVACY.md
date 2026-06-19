# DeskRealm safety and privacy — v0.6.0

## What DeskRealm changes

DeskRealm changes the current user's Windows Desktop Known Folder path so Explorer displays the folder assigned to the active virtual desktop. It does not implement a visual overlay and it does not move files during ordinary switches.

## Safety rules

- Keep `rejectOneDriveDesktop` enabled unless you explicitly accept synchronization risk.
- Back up important Desktop files before first use.
- Keep `scripts\Restore-Desktop.ps1` available.
- Do not enable Desktop auto-arrange when testing coordinate persistence.
- Quit DeskRealm before replacing binaries.

## Performance-pipeline safety

- Registry observer failure is reported; DeskRealm does not silently fall back to periodic polling.
- Persistent worker failure is reported and icon persistence is disabled for the current session; realm switching remains alive.
- Request/response GUIDs prevent a stale worker response from being accepted.
- Direct hotkeys save the source realm before the parallel phase. Navigation and target preparation join at a final GUID barrier; if Windows reaches a different desktop, DeskRealm explicitly restores the actual desktop realm and reports the mismatch instead of leaving the speculative realm active.
- Explorer must expose the exact target realm membership before automatic placement is attempted.
- `E_BOUNDS` / `E_CHANGED_STATE` during live Shell-view replacement are treated only as bounded transient readiness states. `E_FAIL` is also transient only when raised by Shell-view operations inside automatic `restore-when-ready`; manual save/restore and unrelated COM failures remain explicit.
- Startup never silently trusts an already-selected realm folder as proof of correct icon positions. One adaptive restore is required before the process enters its normal no-op path.
- Position verification checks the resulting coordinates.
- The targeted Shell notification is non-destructive; readiness is independently verified.
- Layout, realm and topology-variant locks are preserved.
- Confirming a manual overwrite on a locked layout/realm authorizes only the active display-topology variant. Other variants are integrity-checked before the JSON file is written.
- The 24-variant limit never evicts an existing variant silently; adding a new topology at capacity fails explicitly until the user deletes one.

## Privacy

DeskRealm is local-only. It does not send realm names, icon names, file paths, configuration, logs or usage telemetry to a remote service. Logs remain under `%APPDATA%\DeskRealm` and may contain local paths and icon display names; review them before sharing publicly.
