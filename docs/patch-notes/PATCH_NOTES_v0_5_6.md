# PATCH_NOTES v0.5.6

## Goal

Reduce icon restore misses when the same shortcut exists across multiple DeskRealm realms or when Explorer exposes a different PIDL token for the same human-visible shortcut.

## Implementation

- Captures Shell display name and parsing name through `IShellFolder::GetDisplayNameOf`.
- Adds `identityKeys` to each saved icon layout item.
- Keeps PIDL hash as the primary identity key.
- Adds fallback matching by display/parsing/filename/stem identity.
- Logs each fallback match explicitly.

## Required manual step

Existing v0.5.5 layouts only contain PIDL hashes. Save each realm once after installing v0.5.6 to populate the new identity metadata.
