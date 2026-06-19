# DeskRealm configuration — v0.6.0

Config path:

```text
%APPDATA%\DeskRealm\deskrealm.config.json
```

## Schema version 11

The migration preserves assignments, first-run decisions, direct hotkeys, startup preference and all lock dictionaries. Fixed polling/settle/retry settings from older schemas are retired.

### Performance guardrails

| Field | Default | Valid range | Purpose |
|---|---:|---:|---|
| `iconLayoutWorkerTimeoutMs` | 8000 | 1000–60000 | Maximum duration of one worker command |
| `shellViewReadyTimeoutMs` | 2500 | 250–15000 | Maximum wait for exact target realm membership |
| `iconLayoutRestoreVerificationTimeoutMs` | 1400 | 250–10000 | Maximum position verification window |
| `hotkeyModifierReleaseTimeoutMs` | 1200 | 100–5000 | Maximum wait for physical Win/Ctrl/Shift/Alt release |
| `desktopStepConfirmationTimeoutMs` | 1800 | 250–10000 | Maximum confirmation time for one desktop GUID step |

These are failure guardrails, not mandatory sleeps. DeskRealm proceeds as soon as the required state is observed.

### Example

```json
{
  "version": 11,
  "enabled": true,
  "originalDesktopPath": "C:\\Users\\User\\Desktop",
  "realmsRoot": "C:\\Users\\User\\Desktop\\DeskRealm",
  "syncRealmNamesWithVirtualDesktopNames": true,
  "iconLayoutPersistenceEnabled": true,
  "iconLayoutWorkerTimeoutMs": 8000,
  "iconLayoutDisplayTopologyGuardEnabled": true,
  "shellViewReadyTimeoutMs": 2500,
  "iconLayoutRestoreVerificationTimeoutMs": 1400,
  "desktopHotkeysEnabled": true,
  "hotkeyModifierReleaseTimeoutMs": 1200,
  "desktopStepConfirmationTimeoutMs": 1800,
  "lockedIconLayouts": {},
  "lockedRealms": {},
  "lockedIconLayoutVariants": {}
}
```

Invalid values fail explicitly during config load. DeskRealm does not silently clamp or replace them.

## Layout variant persistence rule

Each virtual desktop layout file can contain up to 24 exact display-topology variants. **Save icon layout now** updates only the variant matching the active `displayTopologyKey`; lock confirmation does not broaden that scope. If the active topology is new and 24 variants already exist, DeskRealm fails explicitly and requires deletion of an obsolete variant. No configuration flag can enable bulk overwrite or silent eviction.
