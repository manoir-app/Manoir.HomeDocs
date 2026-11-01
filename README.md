# Manoir.HomeDocs

Socle du « Manuel de la maison », destiné à fonctionner sur le réseau local.
Ce dépôt contient une API ASP.NET Core 10 et deux interfaces React/TypeScript
distinctes : le site public et le futur back-office.

## État actuel

Cette première étape fournit uniquement des écrans d'attente, le point de santé
et la documentation OpenAPI. Il n'y a pas encore de compte administrateur,
d'authentification, de gestion de contenu, de base SQLite ni de connecteur.
L'écran `/admin/` est donc public et ne doit pas recevoir de données sensibles.
Le back-office utilise le kit de composants MaNoir ; le site public possède ses
propres composants.

## Prérequis

- SDK .NET 10 ;
- Node.js 24 et npm ;
- Docker avec Compose pour le déploiement ;
- un accès en lecture à `@manoir-app/core-admin-ui-kit` sur GitHub Packages
  pendant l'installation et la construction.

Le registre npm public, GitHub Packages, NuGet et les registres d'images peuvent
être nécessaires lors de l'installation et du build. Une fois l'application
construite et lancée, elle ne nécessite pas d'accès à Internet.

Le kit est fixé à la même version que dans HomeAutomation. Pour l'installer,
configurer localement `NODE_AUTH_TOKEN` avec un jeton GitHub autorisé à lire le
package. Ne pas enregistrer le jeton dans le dépôt, ni le transmettre dans le
code, les journaux ou la conversation. Sous PowerShell, il est possible de
le saisir pour le terminal courant sans l'afficher :

```powershell
$env:NODE_AUTH_TOKEN = [System.Net.NetworkCredential]::new('', (Read-Host 'Jeton GitHub Packages' -AsSecureString)).Password
npm ci
```

Le fichier `.npmrc` ne contient que la référence à cette variable. Compose
transmet le jeton au build par un secret BuildKit, sans l'inclure dans l'image.
Le jeton n'est pas nécessaire au lancement d'une image déjà construite.
Sous Windows, si le jeton a été enregistré comme variable utilisateur après
l'ouverture du terminal, le charger dans ce terminal avec :

```powershell
$env:NODE_AUTH_TOKEN = [Environment]::GetEnvironmentVariable('NODE_AUTH_TOKEN', 'User')
```

## Développement

Depuis la racine :

```powershell
npm ci
npm run dev
```

Ouvrir `http://localhost:5173/` pour le site public ou
`http://localhost:5173/admin/` pour le shell d'administration. La commande
démarre aussi l'API sur `http://localhost:5100` et Vite pour l'administration
sur `http://localhost:5174/admin/`. Le proxy Vite transmet `/api`, `/health`,
`/openapi` et `/admin` aux services correspondants.

Pour démarrer les processus dans des terminaux séparés :

```powershell
npm run dev:api
npm run dev:public
npm run dev:admin
```

L'API expose `/health` et `/openapi/v1.json`. En développement, les interfaces
sont servies par Vite ; en production, ASP.NET sert leurs fichiers compilés.
Les chemins inconnus sous `/api` ou `/openapi` et les ressources absentes ne
doivent pas être redirigés vers les pages React.

## Docker Compose

Avec `NODE_AUTH_TOKEN` configuré dans le terminal :

```powershell
docker compose up -d --build
docker compose ps
docker compose logs homedocs
```

Le site est accessible sur `http://localhost:8080/` et son point de santé sur
`http://localhost:8080/health`. Définir `HOMEDOCS_PORT` avant le démarrage pour
modifier le port hôte. Une image déjà construite peut être lancée sans build :

```powershell
docker compose up -d --no-build
```

Le volume nommé `homedocs-data` est monté sur `/data` et conservé lors d'un
arrêt ou d'une recréation du conteneur. Il contient les répertoires `database`,
`content`, `media`, `themes`, `css`, `backups` et `keys` ; ces répertoires
préparent les étapes suivantes et ne constituent pas encore une fonctionnalité
de stockage ou de sauvegarde. Les clés ASP.NET Data Protection sont persistées
dans `keys`. En développement, `HomeDocs__DataPath` permet de choisir un autre
répertoire ; par défaut, il est situé dans `apps/Manoir.HomeDocs.Api/data`.

Les journaux de l'API sont émis au format JSON. Le conteneur est relancé selon
la politique `unless-stopped` et son healthcheck interroge `/health`.

## Vérifications

```powershell
dotnet test Manoir.HomeDocs.slnx
dotnet format Manoir.HomeDocs.slnx --verify-no-changes
npm run lint
npm run format:check
npm run typecheck
npm test
npm run build
docker compose config --quiet
```

Les tests .NET couvrent le healthcheck, OpenAPI, la séparation des deux
interfaces et les routes ou ressources absentes. Les tests React couvrent les
écrans minimaux. Les fonctionnalités futures doivent être accompagnées de
leurs propres tests.

Vérifications effectuées : 17 tests .NET, deux tests React, lint, formatage,
typecheck et build réussis. Le démarrage de développement et le healthcheck ont
été vérifiés, ainsi que le build et le lancement Compose, la modification du port
et la persistance du volume après recréation du conteneur. Les deux interfaces
ont été affichées en navigateur en bloquant les requêtes vers des origines
externes. L'image construite a également été redémarrée sans jeton.

`npm audit --omit=dev` ne signale aucune vulnérabilité. L'audit complet signale
des vulnérabilités modérées liées à `minimatch` dans les dépendances de lint ;
elles ne sont pas incluses dans l'image d'exécution. Ne pas utiliser
`npm audit fix --force` sans vérifier les changements de versions proposés.
