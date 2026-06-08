# DeskRealm v0.4.1 — Hotfix SendInput + hotkey timing

## Correctifs

- Correction du P/Invoke `SendInput` : la structure `INPUT` déclare maintenant les trois membres natifs de l’union (`MOUSEINPUT`, `KEYBDINPUT`, `HARDWAREINPUT`).
- Le bug v0.4.0 produisait `Win32Error=87` car `Marshal.SizeOf<INPUT>()` pouvait être trop petit sur x64.
- Ajout d’un log `INPUT cbSize=...` pour diagnostiquer immédiatement la taille utilisée par `SendInput`.
- Ajout de `hotkeyInitialDelayMs` pour éviter que le Shift du raccourci `Win+Shift+...` reste appuyé au moment où DeskRealm simule `Win+Ctrl+Left/Right`.

## Config ajoutée

```json
"hotkeyInitialDelayMs": 180
```

## Note

Windows fournit déjà les raccourcis `Win+Ctrl+Left/Right`, mais ils naviguent uniquement de proche en proche. DeskRealm garde donc les hotkeys directs configurables en calculant le nombre de pas nécessaires.
