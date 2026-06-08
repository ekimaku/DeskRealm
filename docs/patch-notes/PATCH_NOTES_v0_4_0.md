# DeskRealm v0.4.0 — Hotkeys + démarrage Windows

## Ajouts

- Raccourcis clavier globaux configurables pour switcher directement vers les bureaux virtuels par numéro.
- Defaults :
  - `Win+Shift+W` -> bureau 1
  - `Win+Shift+X` -> bureau 2
  - `Win+Shift+C` -> bureau 3
  - `Win+Shift+V` -> bureau 4
  - `Win+Shift+B` -> bureau 5
  - `Win+Shift+N` -> bureau 6
- Service `GlobalHotkeyService` basé sur `RegisterHotKey` et `WM_HOTKEY`.
- Navigation vers le bureau cible via les raccourcis Windows officiels `Win+Ctrl+Left/Right`, envoyés par `SendInput`.
- Attente stricte de confirmation registry du bureau cible avant de changer le Known Folder Desktop.
- Action tray `Reload hotkeys from config`.
- Option tray `Démarrer avec Windows`.
- Service `StartupService` via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Sécurité / no fallback

- Si un raccourci est refusé par Windows, DeskRealm le log et l'affiche, mais n'invente aucun autre raccourci.
- Si le bureau cible n'existe pas, DeskRealm affiche une erreur explicite.
- Si Windows ne confirme pas le bureau cible dans `hotkeySwitchSettleTimeoutMs`, DeskRealm stoppe l'action hotkey avec erreur claire.
- Le switch Desktop folder / realm / icon layout reste contrôlé par la logique validée v0.3.3.
