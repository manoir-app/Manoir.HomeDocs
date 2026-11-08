# Manoir.HomeDocs

Socle du « Manuel de la maison », destiné à fonctionner sur le réseau local.
Ce dépôt contient une API ASP.NET Core 10 et deux interfaces React/TypeScript
distinctes : le site public et le back-office.

## État actuel

Le compte administrateur, la gestion Markdown, l’arborescence et les contextes
sont disponibles. Le lecteur public applique les profils d’affichage et la
protection des pages par PIN ; une recherche locale (SQLite FTS5) filtre les
résultats par contexte, gère synonymes et fautes de frappe, et n’expose jamais
le corps d’une page protégée avant saisie du PIN. La bibliothèque de médias
(images PNG, JPEG, GIF, WebP et PDF, 20 Mo maximum) se gère dans l’administration :
recherche, classement par dossier, titre et texte alternatif, insertion dans
l’éditeur sans saisir de chemin, liste des pages utilisatrices et suppression
protégée. Chaque page garde son historique (création, modifications, publications)
et une version antérieure peut être restaurée sans changer l’adresse de la page ;
les liens sortants, entrants et cassés sont signalés dans l’éditeur ; les contenus fictifs ne sont installés qu’après
une action explicite dans l’administration. Les connecteurs restent à venir.
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
aussi le jeton antiforgery décrit par `/api/admin/auth/antiforgery`. Les pages
publiées sont consultables via l’API publique ; les brouillons restent réservés
à l’administration.

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
| `GET /api/admin/tree/` | Lister les nœuds de l’arborescence |
| `GET /api/admin/tree/nodes/{id}/target` | Résoudre un élément vers l’identifiant de sa page |
| `POST /api/admin/tree/links` | Ajouter un lien vers une page existante |
| `PUT /api/admin/tree/nodes/{id}` | Déplacer et réordonner un nœud |
| `DELETE /api/admin/tree/nodes/{id}` | Retirer un lien sans supprimer sa page cible |
| `GET /api/admin/contexts/` | Lister les contextes |
| `POST /api/admin/contexts/` | Créer un contexte |
| `PUT /api/admin/contexts/{id}` | Modifier nom, identifiant ou racine |
| `DELETE /api/admin/contexts/{id}` | Supprimer un contexte inutilisé |
| `POST /api/admin/demo/install` | Installer explicitement les contenus fictifs |
| `GET /api/public/navigation?context={id}&profile={profile}[&device={id}]` | Lire la racine et les pages visibles du contexte |
| `GET /api/public/pages/{slug}?context={id}&profile={profile}[&device={id}]` | Lire une page ou être redirigé vers la racine |
| `POST /api/public/access/pin` | Vérifier le PIN et autoriser le contexte |
| `GET /api/public/access/status?context={id}&profile={profile}` | Renouveler et vérifier l’autorisation temporaire |
| `POST /api/public/access/logout` | Révoquer l’autorisation PIN |

Par exemple, une création reçoit un objet JSON de cette forme :

```json
{
  "page": {
    "title": "Cuisine",
    "slug": "cuisine",
    "category": "piece",
    "contexts": ["invites"],
    "requiresAuthentication": false,
    "markdown": "# Cuisine\n"
  },
  "placement": {
    "parentNodeId": null
  }
}
```

`parentNodeId: null` place la page à la racine ; sinon, choisir l’identifiant
d’un nœud parent existant. Ce choix est obligatoire. La modification reçoit
`{ "revision": 1, "page": { ... } }`, avec le même objet de page. La publication reçoit
`{ "revision": 1, "isPublished": true }` et la duplication
`{ "title": "Copie", "slug": "copie", "placement": { "parentNodeId": null } }`.

La création et la duplication produisent des brouillons. Une modification d'une
page déjà publiée prend effet dès son enregistrement. Chaque page contient une
révision : la modification, la publication et la suppression doivent fournir la
révision lue ; une révision périmée renvoie 409. Un slug publié modifié conserve
une redirection, mais la réutilisation de son ancien slug a priorité sur celle-ci.
L'historique et la restauration des versions relèvent d'une étape ultérieure.

## Arborescence et liens

Depuis **Gérer l’arborescence**, organiser les pages à la racine ou sous un nœud
existant. Il n’y a pas de migration de contenu ; toute nouvelle page demande un
emplacement explicite. Dans le JSON de création,
`placement.parentNodeId` vaut `null` pour la racine ou l’identifiant du nœud
parent.

Un lien est un nœud séparé qui référence directement une page réelle ; il ne
copie pas son Markdown et ne peut pas cibler un autre lien. La même page peut
ainsi apparaître sous plusieurs parents. Déplacer un nœud sous lui-même ou l’un
de ses descendants est refusé ; un simple lien vers une page parente reste
possible. Seuls les liens peuvent être retirés de l’arborescence ; une page est
supprimée depuis son éditeur. La suppression d’une page qui contient des
sous-pages est refusée jusqu’à leur déplacement ou leur retrait.
Les commandes Monter, Descendre, Rendre sous-page et Remonter d’un niveau
offrent une alternative clavier au glisser-déposer. Le contenu d’un nœud se
modifie en ouvrant sa page cible.

## Contextes et consultation locale

Aucun contexte n’est créé par défaut. Depuis **Gérer les contextes**, créer un
identifiant stable et un nom ; une racine peut être laissée vide et définie plus
tard. Une page à contextes vides est disponible partout ; sinon ses identifiants
doivent désigner des contextes existants.

Toute invocation publique doit fournir un profil parmi `desktop`, `mobile`,
`tablet-wall`, `tablet-dock` et `tv`. Le paramètre `device` est facultatif et
n’influence pas les droits. Un profil absent ou inconnu, ou un contexte absent
ou inconnu, répond 404. Les liens du lecteur, les URL de Markdown local et les
redirections conservent le contexte, le profil et le device.

`GET /api/public/navigation?context={id}&profile={profile}` retourne la racine
et ses pages publiées, disponibles dans le contexte. Un contexte sans racine
répond 404 pour la navigation, mais l’accès direct à une page disponible reste
possible. Si une page demandée n’est pas disponible dans le contexte, l’API
redirige vers la racine lorsqu’elle existe.

Une page protégée ne retourne pas son Markdown avant vérification du PIN. Le
cookie d’autorisation est HttpOnly, lié au contexte, expire après 30 secondes
d’inactivité et est renouvelé lors d’une activité vérifiée par le serveur. Le
PIN est limité à cinq tentatives par minute et par adresse IP. Le HTML brut et
les liens externes ne sont pas interprétés par le lecteur.

## Contenus fictifs

Aucun contexte ni contenu n’est créé au démarrage. Depuis la page d’accueil de
l’administration, utiliser **Installer les données fictives** pour ajouter les
contextes `invites`, `menage` et `habitant`, avec des pages de démonstration
explicitement marquées comme fictives. L’opération peut être relancée sans
dupliquer ni remplacer les données existantes ; un conflit d’identifiant est
signalé sans écraser les pages ou contextes concernés.

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
adresse IP. Une page protégée ne retourne son contenu qu’après vérification du
PIN ; l’autorisation temporaire est liée au contexte.

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
conflits de modification sont signalés et demandent de recharger la page. Une
page publiée est consultable sur le site public selon ses contextes et son
éventuelle protection par PIN.

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
l'authentification, les refus, la protection PIN, la limitation des tentatives
et l'installation idempotente des données fictives. Les tests React couvrent
l'onboarding, l’administration, l’action d’installation explicite, le lecteur
public, le rendu Markdown sûr, les liens contextualisés et le verrouillage PIN.

Le package du kit MaNoir `0.1.78` publie des chemins de déclarations TypeScript
incorrects dans son point d'entrée ; `ui/admin/tsconfig.json` référence ses
déclarations internes pour le contrôle des types. Le bundler utilise toujours
le point d'entrée normal. Retirer cet alias lorsque le package sera corrigé.

`npm audit --omit=dev` ne signale aucune vulnérabilité. L'audit complet signale
des vulnérabilités modérées liées à `minimatch` dans les dépendances de lint ;
elles ne sont pas incluses dans l'image d'exécution. Ne pas utiliser
`npm audit fix --force` sans vérifier les changements de versions proposés.

## Publier le backlog dans GitHub

Le script PowerShell [publish-backlog-issues.ps1](./scripts/publish-backlog-issues.ps1)
crée les issues de [specs/backlog.md](./specs/backlog.md) dans
`manoir-app/Manoir.HomeDocs`. Il nécessite [GitHub CLI](https://cli.github.com/)
et une authentification autorisée à créer et modifier les issues du dépôt :

```powershell
winget install --id GitHub.cli --exact
# Ouvrir un nouveau terminal après l'installation.
gh auth login --hostname github.com --web
.\scripts\publish-backlog-issues.ps1 -Preview
.\scripts\publish-backlog-issues.ps1
```

Le script reconnaît les issues existantes à leur identifiant `HD-xx`, y compris
si elles sont fermées, et ne les recrée pas. Il ne modifie les descriptions
que pour compléter les liens entre issues créées par ce script. En cas
d'interruption, relancez-le ; ne lancez pas plusieurs exemplaires en parallèle.