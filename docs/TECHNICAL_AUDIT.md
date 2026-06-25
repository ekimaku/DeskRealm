# Technical audit — v0.7.0 release freeze

## Release posture

v0.7.0 is a Realm Studio release freeze. The public objective is a stable WinUI 3 management surface around the already-validated Windows virtual-desktop/Desktop Known Folder engine. This audit intentionally separates release hardening from later architectural refactoring.

## Current platform decisions

| Area | Current choice | Release decision |
|---|---|---|
| .NET SDK | `10.0.301` via `global.json` | Retain. It is the current .NET 10 LTS SDK patch at the audit date. |
| Target framework | `net10.0-windows10.0.19041.0` | Retain. It matches the documented Windows 10 2004+ compatibility floor. |
| Windows App SDK | `2.2.0` | Retain. It is the current Stable Windows App SDK release at the audit date. |
| CommunityToolkit.Mvvm | `8.4.2` | Retain. No toolkit migration is justified inside the release freeze. |
| Packaging | unpackaged, self-contained, single-file `win-x64` | Retain. `EnableMsixTooling` activates only for publish because PRI generation is required there. |
| Node/npm/Vite/React/TypeScript/Three/Dexie | not present in this repository | Not applicable. DeskRealm is a C#/WinUI project. |

## Codebase findings

- `DesktopSwitchService` is the dominant integration class at roughly 2,700 lines. It owns Windows virtual-desktop reconciliation, realm assignment, config migrations, archive behavior, locks and rename orchestration. It works, but it is the main future refactoring target.
- `DesktopIconShellService` and `IconLayoutPersistenceService` are large Shell-bound services. They should remain behaviorally frozen until pure decision logic is protected with tests.
- The repository has one app project and no dedicated test project. This is the largest maintainability gap after public v0.7.0.
- The current CI workflow previously granted `contents: write` to every build run. v0.7.0 release hardening scopes write permission to the tag-only release job.
- Candidate-specific repair manifests and textual source preflight checks are retired. Conventional compiler/build/publish checks are the only release gate.

## Known Windows boundaries retained intentionally

- Virtual-desktop rename relies on persisted Explorer metadata and may require an explicit Explorer restart or reboot before Task View refreshes.
- Explorer restart is visible and disruptive by nature; DeskRealm never restarts Explorer silently.
- Wallpaper reconciliation imports readable external state into DeskRealm but does not force Windows to change during a refresh.
- Registry/Shell failures are explicit. No hidden polling fallback, worker restart or private COM integration is introduced.

## Post-v0.7 milestone order

See [ROADMAP.md](ROADMAP.md). The order is deliberate:

1. testability and service boundaries;
2. diagnostics and supportability;
3. distribution/upgrade/compatibility hardening;
4. only then broader product scope.

## Validation status

Static validation is possible in a non-Windows environment: project/XML/JSON/YAML structure, source/package tree and release-document alignment. Windows remains the source of truth for WinUI rendering, Explorer, Task View, icon layouts and self-contained publish. The real release gate is documented in `docs/validation/v0.7.0-release-control.md`.
