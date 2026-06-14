# DeskRealm v0.5.9 — First-run UX + settings UI block To-Do

## Audit de bloc

Objectif : rendre la première exécution plus smooth sans casser le comportement tray-first existant.

Contraintes strictes :
- UI masquée par défaut, sauf au premier lancement.
- UI ouvrable depuis le tray.
- Croix de fermeture = réduction dans le tray, pas arrêt de DeskRealm.
- Arrêt explicite disponible dans l'UI.
- Options déjà disponibles dans le tray accessibles aussi depuis l'UI.
- Édition des raccourcis bureaux depuis l'UI, avec validation stricte et reload immédiat.
- Premier lancement : afficher comment DeskRealm fonctionne + proposer l'association du Desktop Windows actuel à un realm.
- Si l'utilisateur refuse l'association, créer un raccourci visible vers l'ancien Desktop pour éviter qu'il le perde.
- Aucun fallback silencieux : les erreurs sont loguées et affichées.
- Defaults hotkeys remplacés pour éviter Win+Shift+W et Win+Shift+V :
  - bureau 1 = Win+Shift+X
  - bureau 2 = Win+Shift+C
  - bureau 3 = Win+Shift+B
  - bureau 4 = Win+Shift+N

## Implémentation prévue

- [x] Ajouter une fenêtre principale DeskRealm masquée par défaut sauf premier run.
- [x] Intégrer le contenu “comment ça fonctionne” dans cette fenêtre.
- [x] Intégrer la demande d'association du Desktop original à un bureau virtuel.
- [x] Ajouter une action de skip qui marque le wizard comme complété et crée un raccourci vers le Desktop original dans les realms.
- [x] Ajouter édition UI des hotkeys avec validation via HotkeyParser + sauvegarde config + reload hotkeys.
- [x] Reprendre les actions tray dans l'UI : refresh, sync names, save/restore layout, pause/resume, startup Windows, ouvrir realms/config/logs, restaurer Desktop original, quitter DeskRealm.
- [x] Changer les hotkeys par défaut dans RealmConfig.
- [x] Mettre à jour README, CHANGELOG, docs/release-notes/v0.5.9.md, docs/patch-notes/PATCH_NOTES_v0_5_9.md, CONFIGURATION, INSTALLATION, ARCHITECTURE, SAFETY_AND_PRIVACY, TECHNICAL_AUDIT, TODO/SMOKE_TEST.
- [x] Passer la version projet à 0.5.9.
- [x] Smoke test statique si build Windows indisponible dans le sandbox.
- [x] Corriger les warnings nullable WinForms remontés par le build Windows local.
- [x] Produire un ZIP propre avec root folder DeskRealm.

## Validation attendue

- Premier lancement ouvre l'UI.
- L'UI peut être rouverte depuis le tray.
- La croix masque la fenêtre.
- The Quit button really exits DeskRealm.
- Hotkeys éditables et persistants.
- Defaults v0.5.9 sans W/V.
- Original Desktop shortcut is explicitly created when association is skipped.

## Fermeture du bloc

- Bloc implémenté en une passe cohérente.
- Documentation release-ready mise à jour.
- Build .NET non exécuté dans le sandbox Linux car `dotnet` n'est pas installé ; smoke test statique réalisé.
- Build Windows local validé côté Mike, puis cleanup des 4 warnings nullable WinForms dans `DeskRealmMainForm`.
- ZIP propre généré avec root folder `DeskRealm/`.
