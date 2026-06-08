# DeskRealm v0.5.0 — Open-source publication TODO

## Audit tâche complète

Objectif : préparer DeskRealm pour publication GitHub open-source sans modifier le comportement runtime validé en v0.4.1.

Demandes :

- documenter le ZIP ;
- organiser le dépôt pour GitHub ;
- choisir une licence adaptée à une publication open-source ;
- demander à être sourcé/nommé en cas d'utilisation ou d'inspiration ;
- sourcer les inspirations/références techniques existantes ;
- conserver le root ZIP stable `DeskRealm/`.

## Choix licence

- [x] Choix : Apache License 2.0.
- [x] Raison : open-source permissive, reconnue, avec obligation de préserver LICENSE/NOTICE lors des redistributions de l'œuvre ou dérivés contenant le code.
- [x] Ajout de `NOTICE` pour attribution.
- [x] Ajout de `CITATION.cff` pour demande de citation GitHub.
- [x] Ajout d'un guide qui clarifie que la citation pour pure inspiration est une demande, pas une contrainte imposée par Apache-2.0.

## Documentation ajoutée

- [x] README complet.
- [x] LICENSE.
- [x] NOTICE.
- [x] CITATION.cff.
- [x] AUTHORS.md.
- [x] THIRD_PARTY_NOTICES.md.
- [x] CONTRIBUTING.md.
- [x] SECURITY.md.
- [x] CODE_OF_CONDUCT.md.
- [x] CHANGELOG.md.
- [x] docs/ARCHITECTURE.md.
- [x] docs/CONFIGURATION.md.
- [x] docs/SAFETY_AND_PRIVACY.md.
- [x] docs/ATTRIBUTION_GUIDE.md.
- [x] docs/REFERENCES.md.
- [x] docs/GITHUB_RELEASE_CHECKLIST.md.
- [x] GitHub issue templates.
- [x] GitHub PR template.
- [x] .gitignore / .gitattributes.

## Aucun fallback silencieux ajouté

- [x] Aucun comportement runtime modifié.
- [x] Aucun package tiers ajouté.
- [x] Aucun fichier de config/log privé ajouté.

## Smoke test statique

- [x] Vérifier présence des fichiers open-source.
- [x] Vérifier version projet `0.5.0`.
- [x] Vérifier root ZIP `DeskRealm/`.
- [x] Vérifier que les services runtime principaux sont toujours présents.
