# Patch notes v0.2.0

## Ajout principal

DeskRealm synchronise maintenant les noms des dossiers realms avec les noms des bureaux virtuels Windows visibles dans Win+Tab.

Exemple :

```text
Win+Tab : Personnal -> %USERPROFILE%\Desktop\DeskRealm\Personnal
Win+Tab : Work      -> %USERPROFILE%\Desktop\DeskRealm\Work
```

## Comportement important

- Si un GUID de bureau était déjà assigné à `D1`, et que le bureau s'appelle maintenant `Personnal`, DeskRealm renomme le dossier `D1` en `Personnal`.
- Si `D1` n'existe plus mais que `Personnal` existe, DeskRealm adopte `Personnal` et met à jour la config.
- Si `D1` existe et `Personnal` existe aussi, DeskRealm refuse de choisir et affiche/log une erreur de conflit.
- Si deux bureaux virtuels ont le même nom, DeskRealm refuse de générer automatiquement `Work 2` ou autre suffixe silencieux.

## Nouveau menu tray

```text
Sync names now
```

Cette action relit les noms Win+Tab, synchronise les dossiers et rebascule le Desktop courant vers le bon realm.

## Config ajoutée

```json
{
  "syncRealmNamesWithVirtualDesktopNames": true,
  "realmNameMaxLength": 80
}
```

## Sécurité

Le mode strict est conservé : aucun fallback implicite, aucun déplacement massif, aucune résolution silencieuse de conflit.
