# DeskRealm v0.3.2 — Icon layout worker COM hardening

## Cause ciblée

La v0.3.1 prouvait que le crash venait du worker icônes isolé :

```text
exit code -1073741819
```

Ce code correspond à `0xC0000005`, une access violation native Windows.

Le point suspect était l'interop COM Shell utilisée pour récupérer le nom des icônes via `IShellFolder.GetDisplayNameOf` + `STRRET`. La structure `STRRET` contient une union native et une mauvaise déclaration C# peut corrompre la mémoire du worker avant que l'exception soit loggable.

## Correction

- Suppression du chemin `IShellFolder.GetDisplayNameOf` / `STRRET` pour la capture/restauration.
- Utilisation de `IFolderView.Items(..., IID_IShellItemArray, ...)`.
- Lecture des noms via `IShellItem.GetDisplayName(SIGDN_PARENTRELATIVEPARSING)`.
- Conservation de `IFolderView.Item(index)` + `IFolderView.GetItemPosition` pour les positions.
- Conservation de `IFolderView.SelectAndPositionItems` pour la restauration.
- Ajout de logs de phase dans le worker : acquisition `IFolderView`, item count, capture/restauration.

## Politique sans fallback

Aucun layout inventé, aucune fusion silencieuse : si la capture/restauration échoue, DeskRealm loggue l'erreur, désactive explicitement la persistance d'icônes pour la session, et garde le switching des bureaux actif.
