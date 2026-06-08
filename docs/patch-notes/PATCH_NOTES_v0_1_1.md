# DeskRealm v0.1.1 — Patch build

## Correction

Correction de la build C# : `KnownFolderService` passait directement le champ `static readonly Guid FolderIdDesktop` en `ref` à `SHGetKnownFolderPath` et `SHSetKnownFolderPath`.

C# interdit d’utiliser un champ `static readonly` comme valeur `ref` ou `out` hors constructeur statique. La correction crée une copie locale mutable du GUID avant l’appel P/Invoke.

## Fichier modifié

- `src/DeskRealm.App/Services/KnownFolderService.cs`

## Statut test

- Smoke test statique relancé.
- Build Windows réelle à exécuter côté poste Windows avec `scripts/Build-Release.ps1`.
