# DeskRealm v0.3.1 — Icon layout crash guard

## Problem fixed

v0.3.0 could crash/close the main tray application during virtual desktop switches when the experimental icon layout Shell/COM capture or restore failed at native interop level. In that situation no DeskRealm log was produced and `%APPDATA%\DeskRealm\icon-layouts` stayed empty because the process died before the layout JSON could be written.

## Changes

- Added `IconLayoutWorkerClientService`.
- Icon layout save/restore now runs in an isolated worker process using the same executable with `--icon-layout-worker`.
- The main DeskRealm tray app no longer performs the Shell icon capture/restore directly during automatic switching.
- If the worker fails, exits non-zero, or times out, DeskRealm logs the full failure and disables icon layout persistence for the current session.
- Desktop realm switching continues after the icon layout feature is disabled for the session.
- Added global handlers for managed unhandled exceptions.
- File logger now writes with `FileShare.ReadWrite` and retry support to handle main app + worker concurrent log writes.
- Added config field `iconLayoutWorkerTimeoutMs` with default `8000`.

## Important

This patch keeps v0.2/v0.3 desktop switching stable first. If the icon worker still fails on a machine, the new logs should show exactly why instead of closing the app silently.
