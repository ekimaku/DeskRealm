# DeskRealm v0.1.2 — Packaging root fix

## Change

- The ZIP now contains a stable root folder named exactly `DeskRealm`.
- The archive filename may keep the version number, but the extracted folder does not.

## Why

This makes iterative replacement easier: extract over the existing `DeskRealm` folder instead of creating a new versioned folder on every patch.

## Code changes

No runtime code change from v0.1.1.
