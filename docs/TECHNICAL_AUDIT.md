# DeskRealm — Technical audit v0.5.6

## Objectif produit

DeskRealm transforme les bureaux virtuels Windows en espaces de travail réellement séparés : chaque bureau virtuel obtient son propre dossier Desktop, son nom synchronisé depuis Win+Tab, son layout d'icônes et des raccourcis clavier directs.

La ligne v0.5.1 → v0.5.6 a stabilisé la partie la plus sensible : la persistance des positions d'icônes Desktop dans des conditions réelles multi-écrans, résolution variable, DPI / mise à l'échelle et raccourcis répétés sur plusieurs realms.

## Architecture validée

- `VirtualDesktopRegistryService` lit les GUID/noms/order/current desktop depuis Explorer registry.
- `KnownFolderService` redirige le Desktop Known Folder.
- `ShellRefreshService` force Explorer/Shell à rafraîchir l'affichage.
- `DesktopSwitchService` orchestre : détection, assignment, rename, switch, save guards, restore différé, hotkeys et restore-on-exit.
- `DisplayTopologyService` capture la topologie d'affichage : écrans actifs, bounds, résolution, orientation et DPI effectif / scale.
- `DesktopIconShellService` capture/restaure les positions via `IFolderView` et `SelectAndPositionItems`.
- `IconLayoutPersistenceService` stocke plusieurs variants de layout par bureau virtuel.
- `IconLayoutWorkerClientService` isole les opérations Shell/COM d'icônes dans un worker.
- `GlobalHotkeyService` enregistre les raccourcis globaux avec `RegisterHotKey`.
- `VirtualDesktopNavigatorService` bascule vers un desktop numéroté via navigation gauche/droite.
- `KeyboardInputService` envoie `Win+Ctrl+Left/Right` avec `SendInput`.
- `StartupService` gère le démarrage Windows via HKCU Run key.

## Évolution technique récente

### v0.5.1 — Quiet icon layout

- Suppression du polling Shell périodique par défaut.
- Migration config v2 : `iconLayoutAutoSaveEnabled=false`.
- Sauvegarde conservée sur switch, sauvegarde manuelle et restore-on-exit.
- Suppression du refresh Shell post-restauration qui pouvait provoquer un redraw/reflow inutile.

### v0.5.2 — Anti-contamination bureau/path

- Refus de sauvegarder un layout si le Desktop Known Folder actif correspond à un realm différent du bureau virtuel Windows courant.
- Protection contre le cas où Windows a déjà changé de bureau virtuel mais Explorer affiche encore le realm précédent.

### v0.5.3 — Display topology variants

- Introduction des variants par topologie : écrans actifs, virtual bounds, résolution, orientation, DPI / scale.
- Stockage des positions absolues + relatives à l'écran d'origine.
- Refus temporaire des saves pendant changement d'écran/résolution/scale.
- Activation du mode DPI `PerMonitorV2` côté projet.

### v0.5.4 — Restore différé après switch

- Restauration d'icônes différée après changement de bureau/folder pour laisser Explorer finir d'afficher le realm cible.
- Garde anti-save pendant pending restore.
- Retentatives de restore après délai court pour absorber les reflows Explorer.

### v0.5.5 — Verified restore

- Placement en groupes plus petits.
- Vérification des positions réelles après restore.
- Retry ciblé sur les icônes qui n'ont pas bougé.
- Logs explicites des icônes encore non conformes.

### v0.5.6 — Shell identity fallback

- Enrichissement des layouts avec `shellDisplayName`, `shellParsingName` et `identityKeys`.
- Matching exact PIDL d'abord, puis fallback par identité Shell lisible.
- Résolution des cas où la même icône/raccourci existe sur plusieurs realms mais obtient un PIDL différent selon le contexte Explorer.

## No fallback implicite

- Pas de bascule vers D1 si le bureau courant est illisible.
- Pas de création de nom alternatif en cas de conflit de dossiers.
- Pas de remapping hotkey implicite si Windows refuse une combinaison.
- Pas de fusion automatique de folders realms.
- Pas de sauvegarde layout si la topologie ou le bureau courant est ambigu.
- Pas de startup système global machine ; uniquement HKCU utilisateur courant.
- Pas de contrainte de licence ajoutée qui contredirait Apache-2.0.

## Tests terrain validés

Validé par usage réel sur la machine d'origine :

- switch entre plusieurs bureaux virtuels ;
- mêmes icônes présentes sur plusieurs realms avec positions différentes ;
- écran principal éteint/rallumé pendant que l'écran secondaire reste actif ;
- changement de résolution ;
- changement de DPI / mise à l'échelle ;
- retour automatique au bon variant de layout selon le contexte ;
- correction du cas où certaines icônes étaient `not found` via fallback d'identité Shell.

## Points à surveiller

- La logique dépend toujours d'Explorer comme Shell Desktop.
- Les layouts existants pré-v0.5.6 doivent idéalement être resauvegardés une fois par realm pour inclure les nouvelles clés d'identité.
- Une future UI de settings/diagnostic serait utile pour afficher le variant de topologie actif et les icônes non résolues.

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
