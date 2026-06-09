# DeskRealm — Technical audit v0.5.1

## Objectif produit

DeskRealm transforme les bureaux virtuels Windows en espaces de travail réellement séparés : chaque bureau virtuel obtient son propre dossier Desktop, son nom synchronisé depuis Win+Tab, son layout d'icônes et des raccourcis clavier directs.

v0.5.1 corrige le modèle de persistance des icônes : suppression du polling Shell périodique par défaut, sauvegarde sur switch/exit, et suppression du refresh Shell après repositionnement des icônes.

## Architecture validée

- `VirtualDesktopRegistryService` lit les GUID/noms/order/current desktop depuis Explorer registry.
- `KnownFolderService` redirige le Desktop Known Folder.
- `ShellRefreshService` force Explorer/Shell à rafraîchir l'affichage.
- `DesktopSwitchService` orchestre : détection, assignment, rename, switch, icon save/restore, save-on-switch et restore-on-exit.
- `IconLayoutWorkerClientService` isole les opérations Shell/COM d'icônes dans un worker.
- `GlobalHotkeyService` enregistre les raccourcis globaux avec `RegisterHotKey`.
- `VirtualDesktopNavigatorService` bascule vers un desktop numéroté via navigation gauche/droite.
- `KeyboardInputService` envoie `Win+Ctrl+Left/Right` avec `SendInput`.
- `StartupService` gère le démarrage Windows via HKCU Run key.

## Choix licence

Licence retenue : Apache License 2.0.

Raisons :

- licence open-source permissive ;
- obligation de préserver la licence et les notices d'attribution lors de la redistribution de l'œuvre ou de dérivés contenant le code ;
- compatible avec un `NOTICE` pour demander une attribution claire ;
- compatible avec un `CITATION.cff` pour GitHub.

Limite explicitement documentée : une inspiration sans copie de code n'est pas une redistribution de l'œuvre, donc la citation est formulée comme une demande communautaire, pas comme une restriction additionnelle.

## Sources / inspirations documentées

- Microsoft Learn / Win32 APIs.
- Raymond Chen / The Old New Thing pour la stratégie supportée `IFolderView` des positions d'icônes Desktop.
- Meziantou pour l'exploration registry des virtual desktops Windows.
- pmb6tz/windows-desktop-switcher comme prior art des raccourcis directs par numéro.
- Ciantic/VirtualDesktopAccessor comme prior art important, non embarqué.

Voir `docs/REFERENCES.md` et `THIRD_PARTY_NOTICES.md`.

## Correctif v0.5.1

- Polling icônes périodique désactivé par défaut.
- Migration config `version < 2` : `iconLayoutAutoSaveEnabled` est forcé à `false`.
- `Tick()` ne lance plus le worker icônes si le bureau virtuel actif ne change pas.
- La sauvegarde layout reste automatique avant de quitter un realm et avant de restaurer le Desktop original.
- Le refresh Shell après restore d'icônes est retiré pour éviter un redraw/réarrangement juste après le repositionnement.

## No fallback

- Pas de bascule vers D1 si le bureau courant est illisible.
- Pas de création de nom alternatif en cas de conflit de dossiers.
- Pas de remapping hotkey implicite si Windows refuse une combinaison.
- Pas de fusion automatique de layouts/icônes.
- Pas de startup système global machine ; uniquement HKCU utilisateur courant.
- Pas de contrainte de licence ajoutée qui contredirait Apache-2.0.

## Tests statiques v0.5.1

- Présence `LICENSE`, `NOTICE`, `CITATION.cff`.
- Présence `README.md`, `CHANGELOG.md`, `AUTHORS.md`, `THIRD_PARTY_NOTICES.md`.
- Présence docs GitHub / sécurité / config / architecture / attribution.
- Présence `.gitignore`, `.gitattributes`, issue templates, PR template.
- Version projet passée à `0.5.1`.
- Root ZIP stable `DeskRealm/`.
