# Security policy

DeskRealm is a local Windows utility. It has no telemetry and no network features.

## Supported versions

The latest published version is the only supported version.

## Reporting a vulnerability

Open a private security advisory on GitHub if available, or open an issue with limited details and request a private contact channel.

Please do not publish exploit details before the maintainer has had time to review.

## Sensitive behaviors to review carefully

- Desktop Known Folder redirection.
- HKCU Run startup entry.
- Shell/COM Desktop icon capture/restore.
- Synthetic keyboard input through `SendInput`.
- Registry reads for Windows virtual desktop state.

## Non-goals

DeskRealm should not become:

- a remote-control tool;
- a telemetry/analytics app;
- a file synchronization tool;
- an admin/system-wide persistence mechanism;
- a tool that bypasses Windows security prompts.
