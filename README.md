# Manoir.HomeDocs

Socle du « Manuel de la maison », destiné à fonctionner sur le réseau local.
Ce dépôt contient une API ASP.NET Core 10 et deux interfaces React/TypeScript
distinctes : le site public et le back-office.

## État actuel

Le premier compte administrateur peut être créé localement ; les routes et API
d'administration sont protégées côté serveur. Les données de configuration sont
stockées dans SQLite. La gestion de contenu et les connecteurs restent à venir.
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

## Initialisation et sécurité

Au premier lancement, ouvrir `/admin/`, créer le compte administrateur puis se
connecter. Le nom est obligatoire et le mot de passe doit comporter entre 12
et 1 024 caractères. Aucun identifiant ni mot de passe n'est créé par défaut.
L'initialisation n'est possible qu'une fois, même en cas de requêtes simultanées.
Protéger l'accès à l'application avant cette première configuration : toute
personne pouvant y accéder peut créer le premier compte. La récupération ou la
réinitialisation du compte n'est pas encore implémentée.

Les mots de passe sont stockés sous forme de hash ASP.NET Core Identity. La
session utilise un cookie HttpOnly, SameSite Strict, avec expiration glissante
de huit heures. Les opérations sensibles utilisent un jeton antiforgery et les
tentatives d'initialisation et de connexion sont limitées ensemble à dix par
minute et par adresse IP, par instance. Après déconnexion, une nouvelle
authentification est nécessaire. L'interface est disponible en français et en
anglais ; le choix de langue n'est pas encore conservé.

**Sur HTTP, les identifiants et les cookies circulent sans chiffrement.**
N'utiliser HTTP que sur un réseau de confiance ; sur un réseau non fiable,
configurer HTTPS de bout en bout et vérifier les paramètres du proxy et de
l'application avant exposition. Le cookie n'est marqué `Secure` que lorsque
l'application reçoit une requête HTTPS ; la prise en charge d'un proxy TLS
n'est pas configurée par défaut.

La base est créée dans `database/homedocs.db` sous le répertoire de données ;
les migrations SQLite sont appliquées automatiquement au démarrage. Conserver
la base et les clés Data Protection dans les sauvegardes et en limiter l'accès :
les clés enregistrées sur disque ne sont pas chiffrées par l'application.

## Pages Markdown

L'API d'administration `/api/admin/pages` permet de lister, créer, lire,
modifier, dupliquer, publier, remettre en brouillon et supprimer les pages.
Toutes ces opérations exigent une session administrateur ; les écritures exigent
aussi le jeton antiforgery décrit par `/api/admin/auth/antiforgery`. Aucun
endpoint public ne donne accès au contenu à ce stade. L'éditeur et l'affichage
public seront ajoutés ultérieurement.

Une page possède un titre, un slug, une catégorie, une liste de contextes
(vide pour tous), un indicateur de protection et son Markdown. Les catégories
acceptées sont `page`, `piece`, `emplacement`, `procedure`, `equipement`,
`depannage` et `technique`. Les slugs et identifiants de contexte sont composés
de lettres minuscules ASCII, de chiffres et de tirets séparateurs. Le Markdown
est limité à 1 Mio en UTF-8. Les fichiers Markdown lisibles sont conservés dans
`content`, et leurs métadonnées et redirections dans SQLite.

L'API accepte les opérations suivantes :

| Méthode et chemin | Utilisation |
| --- | --- |
| `GET /api/admin/pages/` | Lister les pages, brouillons compris |
| `GET /api/admin/pages/{id}` | Lire une page et sa révision |
| `POST /api/admin/pages/` | Créer un brouillon |
| `PUT /api/admin/pages/{id}` | Enregistrer les métadonnées et le Markdown |
| `POST /api/admin/pages/{id}/publication` | Publier ou remettre en brouillon |
| `POST /api/admin/pages/{id}/duplicate` | Dupliquer en fournissant un titre et un slug |
| `DELETE /api/admin/pages/{id}?revision={revision}` | Supprimer une page |

Par exemple, une création reçoit un objet JSON de cette forme :

```json
{
  "title": "Cuisine",
  "slug": "cuisine",
  "category": "piece",
  "contexts": ["invites"],
  "requiresAuthentication": false,
  "markdown": "# Cuisine\n"
}
```

La modification reçoit `{ "revision": 1, "page": { ... } }`, avec le même
objet de page que pour la création. La publication reçoit
`{ "revision": 1, "isPublished": true }` et la duplication
`{ "title": "Copie", "slug": "copie" }`.

La création et la duplication produisent des brouillons. Une modification d'une
page déjà publiée prend effet dès son enregistrement. Chaque page contient une
révision : la modification, la publication et la suppression doivent fournir la
révision lue ; une révision périmée renvoie 409. Un slug publié modifié conserve
une redirection, mais la réutilisation de son ancien slug a priorité sur celle-ci.
L'historique et la restauration des versions relèvent d'une étape ultérieure.

Pour une sauvegarde cohérente, arrêter l'application avant de copier ensemble
les répertoires `database` et `content`, ainsi que les autres données
persistantes nécessaires. La copie de la seule base ne sauvegarde pas les pages.
Après une interruption ou un échec d'écriture, des fichiers non référencés
peuvent être déplacés dans `content/quarantine` avec un avertissement dans les
journaux. Ils ne sont pas publiés et peuvent contenir des données sensibles :
examiner ce répertoire avant de supprimer manuellement les fichiers inutiles.
Une référence SQLite vers un fichier manquant ou une entrée inattendue dans
`content` provoque une erreur explicite et nécessite une intervention.

## Paramètres de l’administration

Depuis `/admin/`, ouvrir les paramètres pour accéder aux préférences UI, à la
sécurité et aux pages d’intégration. Ces pages et leurs API sont réservées à la
session administrateur. Les préférences (actuellement la langue `fr` ou `en`)
sont enregistrées avec le compte ; elles prennent priorité sur la langue du
navigateur. La migration ajoute ces champs sans réinitialiser le compte existant.

La page Sécurité permet de modifier le nom du compte, le mot de passe et le PIN
commun. Chaque changement demande le mot de passe actuel. Le nouveau mot de passe
doit contenir de 12 à 1 024 caractères ; laisser ses deux champs vides conserve
le mot de passe existant. Le PIN est exactement composé de six chiffres ; il
peut être supprimé depuis la même page. Le PIN est stocké sous forme de hash
ASP.NET Core Identity et seul son état configuré ou non est renvoyé à l’interface.
Les modifications sensibles sont limitées à dix requêtes par minute et par
adresse IP. La vérification et l’usage du PIN pour protéger les pages ne sont
pas encore disponibles ; ils relèvent de HD-15.

La page Home Assistant est un emplacement réservé : aucun jeton ni paramètre de
connexion n’y est stocké et aucun test de connexion n’est disponible. La
configuration effective et les autres intégrations seront ajoutées dans des
étapes ultérieures.

## Créer et publier une page

Après connexion, ouvrir **Gérer les contenus** puis **Créer une page**. Saisir un
titre, un slug stable en minuscules ASCII (`a-z`, `0-9` et tirets), une catégorie
et éventuellement une liste de contextes séparés par des virgules. Le contenu
s’édite dans CodeMirror avec coloration Markdown et aperçu. L’aperçu ignore le
HTML brut et ne l’exécute pas.

L’enregistrement crée un brouillon ; une page déjà publiée est modifiée dès
l’enregistrement. Publier ou repasser en brouillon se fait séparément. Les
conflits de modification sont signalés et demandent de recharger la page. La
case de protection enregistre les métadonnées, mais le contrôle par PIN et la
consultation publique ne sont pas encore disponibles (HD-15 et HD-07).

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
préparent les étapes suivantes, à l'exception de `database` et `keys`, utilisés
par SQLite et les clés ASP.NET Data Protection. Ils ne constituent pas une
fonctionnalité de sauvegarde. En développement, `HomeDocs__DataPath` permet de choisir un autre
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

Les tests .NET couvrent notamment les migrations SQLite, la concurrence,
l'authentification, les refus et la persistance après redémarrage. Les tests
React couvrent l'onboarding, la connexion, la déconnexion et les erreurs.

Le package du kit MaNoir `0.1.78` publie des chemins de déclarations TypeScript
incorrects dans son point d'entrée ; `ui/admin/tsconfig.json` référence ses
déclarations internes pour le contrôle des types. Le bundler utilise toujours
le point d'entrée normal. Retirer cet alias lorsque le package sera corrigé.

`npm audit --omit=dev` ne signale aucune vulnérabilité. L'audit complet signale
des vulnérabilités modérées liées à `minimatch` dans les dépendances de lint ;
elles ne sont pas incluses dans l'image d'exécution. Ne pas utiliser
`npm audit fix --force` sans vérifier les changements de versions proposés.
