# Patch notes v0.1.3

## Correction principale

DeskRealm v0.1.2 arrêtait le watcher si `CurrentVirtualDesktop` n'était pas exposé sous :

```text
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\<SessionId>\VirtualDesktops
```

Sur certaines builds Windows 11, le GUID du bureau virtuel courant peut être exposé sous :

```text
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops
```

La v0.1.3 ajoute donc une détection stricte multi-source :

1. `Explorer\VirtualDesktops\CurrentVirtualDesktop`
2. `Explorer\SessionInfo\<Process.SessionId>\VirtualDesktops\CurrentVirtualDesktop`
3. scan contrôlé des autres sous-clés `Explorer\SessionInfo\*\VirtualDesktops`

## Important

Ce n'est pas un fallback silencieux vers `D1`.

DeskRealm accepte uniquement un GUID `CurrentVirtualDesktop` qui correspond réellement à un GUID présent dans `VirtualDesktopIDs`. Si aucune source ne donne un GUID cohérent, le watcher s'arrête et l'erreur est loggée.

## Ajouts

- Parsing `CurrentVirtualDesktop` robuste en `REG_BINARY` 16 bytes.
- Parsing compatible `REG_SZ` au format GUID ou hex 32 caractères.
- Logs indiquant la source registry active utilisée pour détecter le bureau courant.
- Message d'erreur plus actionnable si aucune source registry ne fonctionne.
