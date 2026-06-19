# DeskRealm architecture — v0.6.0

## Runtime components

- `TrayAppContext`: WinForms lifetime, UI dispatcher, one serialized background operation lane shared by tray, hotkeys, registry reconciliation and main-window Shell/state actions, plus reconciliation coalescing.
- `VirtualDesktopChangeMonitor`: registry notification subscriptions for Explorer virtual desktop state.
- `DesktopSwitchService`: realm assignment, safety gates, locks, switch orchestration and degraded-state reporting.
- `VirtualDesktopNavigatorService`: one-step input plus expected-GUID confirmation.
- `KeyboardInputService`: physical modifier release check and native `SendInput` navigation.
- `KnownFolderService`: current-user Desktop Known Folder read/write.
- `ShellRefreshService`: targeted non-blocking Shell directory notification.
- `IconLayoutWorkerClientService`: persistent worker lifecycle and strict request/response correlation.
- `IconLayoutPersistenceService`: topology variants, locks, adaptive readiness, current-variant-only mutation guards and layout file persistence.
- `DesktopIconShellService`: `IFolderView` capture, `IFolderView::Items` / `IEnumIDList` PIDL snapshots, exact view membership, placement and verification.

## Serialized event-driven flow

```text
registry notification / DeskRealm hotkey / display change
                         |
                         v
               coalesced reconcile request
                         |
                         v
          SemaphoreSlim serialized background lane
                         |
      +------------------+------------------+
      |                                     |
 direct hotkey                          native switch
 save source layout                     detect new GUID
 resolve target GUID/realm                   |
 wait modifiers                              |
      |                                      |
      +----------- parallel fork ------------+
      |                                      |
 confirm navigation steps          redirect target Known Folder
      |                            targeted SHChangeNotify
      |                                      |
      |                         persistent STA worker
      |                         enumerates via IEnumIDList
      |                         waits exact realm membership
      |                         places + verifies coordinates
      |                                      |
      +------------------+------------------+
                         v
          final active-GUID + layout-result barrier
                         |
           mismatch => explicit actual-realm compensation
```


Direct hotkeys use a transaction-style split. The source layout is saved first. Because the destination desktop GUID, realm folder and topology variant are already resolved from the hotkey mapping, DeskRealm starts virtual-desktop navigation and target-realm preparation together. The final active desktop GUID is a commit check, not a prerequisite for applying the target realm. If Windows ends on another desktop, DeskRealm explicitly reselects and restores that desktop's realm before surfacing the navigation mismatch.

No periodic WinForms timer controls virtual desktop switching. No global Shell broadcast is required. The UI only refreshes immediately when visible. Generic `E_FAIL` is never globally suppressed: only the automatic transition-aware restore pipeline can reinterpret it as an invalidated live Shell view and retry within the configured readiness deadline.

Startup has a separate one-shot invariant: the first reconciliation must restore the active realm even when the Desktop Known Folder already equals the target path. A matching path proves routing, not layout correctness. This makes recovery resilient when Windows ended the previous process without running DeskRealm's explicit quit path.

## Worker protocol

The current executable starts once with `--icon-layout-worker-server`. Commands and responses are one JSON object per line, encoded as strict UTF-8 without BOM and correlated by GUID. The worker is not silently restarted after protocol or process failure.

Manual save uses the dedicated `save-current-variant` command. The persistence layer identifies the exact active `displayTopologyKey`, replaces that one list entry in place (or inserts a new variant when capacity allows), fingerprints every excluded variant, serializes the full document, deserializes it again, and refuses the write unless all non-current variant fingerprints still match. Generic save/baseline paths use the same non-current-variant preservation primitive.

## Data boundaries

- Config: `%APPDATA%\DeskRealm\deskrealm.config.json`
- Logs: `%APPDATA%\DeskRealm\deskrealm.log`
- Icon layouts: `%APPDATA%\DeskRealm\icon-layouts\<desktop-guid>.json`
- Realms: configured under the original Desktop root by default

## UI architecture boundary

The existing code-driven WinForms UI is intentionally preserved for this performance milestone. A future designer/component refactor is a separate milestone so it cannot obscure performance regressions.
