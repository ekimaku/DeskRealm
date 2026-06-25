# TODO — v0.7.0 repository simplification (`_bb` local candidate)

**Status:** Complete

## Goal

Remove the candidate-specific source-repair and text-preflight machinery that started blocking normal development before the .NET compiler could run. Keep the working Realm Studio/runtime implementation, but return local build, run and CI paths to one conventional Windows .NET workflow.

## Audit findings

- `scripts/Test-RealmStudioSource.ps1` had grown into a 400+ line implementation-shape checker. Its assertions depended on local candidate labels and exact source text, so it produced false failures unrelated to a compilable application.
- `migration/v0.7.0_*` stored repeated whole-source template sets and manifests for each local ZIP suffix. This created duplicated source of truth and repair loops.
- `Run-DeskRealm.ps1` and `Build-Release.ps1` exposed repair/delete flags that made normal development depend on migration recovery paths.
- CI executed the same brittle text preflight before compilation.
- The runtime fixes already validated by Mike (registry-backed Realm rename, optional Explorer restart, tray recovery, archive/live-name separation) must remain intact; this cleanup changes repository mechanics, not product behavior.

## Completed block

- [x] Remove candidate source templates, manifests and automatic source-repair lane.
- [x] Remove brittle source-text preflight from local scripts and CI.
- [x] Restore a conventional build flow: clean generated output → RID restore → Release build → self-contained publish → output validation.
- [x] Restore a conventional run flow: clean generated output → `dotnet run` with implicit restore.
- [x] Keep `EnableMsixTooling` conditional on single-file publish, which is required for the unpackaged WinUI publish path.
- [x] Remove local-candidate documentation debris and consolidate the v0.7.0 development record.
- [x] Update README, CHANGELOG, release notes, installation, architecture, configuration, safety, release process, technical audit, smoke test and project TODO.
- [x] Validate repository structure statically: no migration directory, no repair/preflight references, XML/JSON parse, one ZIP root folder, no generated build output.

## Windows validation still required

- [ ] Run `./scripts/Build-Release.ps1` on Windows.
- [ ] Launch `./dist/DeskRealm/DeskRealm.App.exe`.
- [ ] Smoke-test: rename → Explorer restart → tray reappears → tray menu / Realm Studio / hotkeys / clean tray exit.
- [ ] Smoke-test archived realm reuse and live-name conflict UX after the build pipeline is confirmed stable.
