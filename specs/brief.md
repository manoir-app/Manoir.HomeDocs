> **Brief en cours d’élaboration.** Ce document rassemble les décisions prises et les besoins restant à préciser. Les instructions d’implémentation ci-dessous décrivent le travail futur : ne pas commencer l’implémentation avant validation du brief.

Le périmètre fonctionnel décrit dans ce document est conservé dans son ensemble, mais son développement sera progressif, par étapes. Le découpage et l’ordre des livraisons restent à définir ; les fonctionnalités prévues plus tard ne doivent pas être considérées comme abandonnées.

Un [backlog prévisionnel](./backlog.md) regroupe les travaux proposés en vue de leur publication ultérieure sous forme d’issues GitHub.

Tu es chargé de concevoir et d’implémenter une application complète appelée « Manuel de la maison ».

Travaille directement dans ce dépôt.

Ne te contente pas de fournir un plan, des exemples ou des extraits de code. Crée réellement les fichiers, installe les dépendances, implémente les fonctionnalités, exécute les vérifications et corrige les erreurs.

Prends les décisions techniques raisonnables sans demander une validation à chaque étape.

L’application doit être suffisamment propre et structurée pour servir de véritable base de développement, sans chercher à implémenter prématurément toutes les évolutions possibles.

Inspecte d’abord le dépôt existant et respecte autant que possible ses conventions, son organisation et ses choix déjà établis.

Tu es libre d’organiser les projets, fichiers, dossiers et composants comme tu le juges pertinent. Ne réorganise pas inutilement un dépôt existant si son organisation permet de répondre correctement au besoin.

# 1. Vision du produit

L’application n’est pas un wiki traditionnel.

Elle doit constituer l’interface d’aide, de documentation et de pilotage de la maison.

Elle doit permettre notamment de :

- trouver rapidement où est rangé un objet ;
- savoir où ranger ou jeter quelque chose ;
- consulter les procédures courantes de la maison ;
- consulter les instructions destinées à la personne chargée du ménage ;
- afficher des fiches pratiques pour les appareils électroménagers ;
- accéder à des procédures simples de dépannage ;
- documenter le système domotique et l’infrastructure technique ;
- afficher des informations provenant du système domotique ;
- déclencher des actions domotiques autorisées ;
- ouvrir directement une page depuis Home Assistant ou un QR code ;
- annoncer sa présence par MQTT, puis exposer progressivement des fonctions et des états à Home Assistant par MQTT Discovery.

Le site sera principalement utilisé sur :

- une tablette tactile 10 pouces encastrée dans un mur ;
- une Pixel Tablet installée sur son support ;
- une Fire TV dans la cuisine, pilotée à la télécommande ;
- un téléphone ;
- un ordinateur.

L’application doit proposer :

- un front public destiné à la consultation ;
- un back-office d’administration ;
- une API serveur ;
- un moteur de rendu Markdown enrichi ;
- un connecteur Home Assistant ;
- un connecteur MQTT ;
- un système de thèmes et de profils d’affichage ;
- des contextes d’utilisation et un catalogue intégré d’actions domotiques.

# 2. Principes de conception

L’interface publique doit être :

- en français ;
- immédiatement compréhensible ;
- chaleureuse et domestique ;
- tactile ;
- adaptée à une consultation debout et rapide ;
- utilisable à distance sur une télévision ;
- responsive ;
- utilisable sur le réseau local ;
- capable de fonctionner sans accès Internet ;
- lisible par une personne non technique ;
- accessible au clavier et à la télécommande.

Éviter :

- l’apparence d’un portail d’administration sur le front public ;
- l’apparence d’une documentation pour développeurs ;
- les tableaux de bord surchargés ;
- les menus trop complexes ;
- les petits boutons ;
- les interactions dépendant du survol ;
- les animations inutiles ;
- les gestes tactiles obligatoires.

La page d’accueil du contexte actif doit permettre d’atteindre l’information recherchée en moins de deux ou trois interactions.

Le site doit proposer deux niveaux de contenu :

1. Le contenu utilisateur, simple et visible par défaut.
2. La documentation technique, accessible depuis une entrée distincte et plus discrète.

# 3. Stack technique

Utiliser les technologies suivantes :

- ASP.NET Core 10 ;
- C# ;
- nullable reference types activé ;
- React ;
- TypeScript en mode strict ;
- Vite ;
- SQLite ;
- Entity Framework Core ;
- fichiers Markdown comme source principale du contenu éditorial ;
- SignalR pour les mises à jour en temps réel ;
- MQTTnet pour la connexion MQTT ;
- Docker ;
- Docker Compose ;
- OpenAPI pour documenter l’API ;
- tests backend et frontend.

Ne pas utiliser Astro comme architecture principale.

Le front public et le back-office peuvent être :

- deux applications React distinctes ;
- ou deux zones clairement séparées d’une même application React.

Choisir l’approche offrant le meilleur compromis entre :

- simplicité ;
- maintenabilité ;
- partage des composants ;
- sécurité ;
- expérience de développement ;
- facilité de déploiement.

L’organisation physique du dépôt, le nombre de projets et le découpage des dossiers sont laissés à ton appréciation.

Séparer néanmoins clairement les responsabilités suivantes dans le code :

- domaine métier ;
- logique applicative ;
- persistance ;
- API ;
- connecteurs externes ;
- interface publique ;
- administration ;
- tests.

Cette séparation peut être réalisée par projets, modules, espaces de noms ou composants selon ce qui est le plus adapté au dépôt.

## 3.1 Conventions MaNoir et abstraction de persistance

S’inspirer de l’organisation des applications MaNoir existantes :

- un projet ou package **Contracts** contenant les modèles et contrats publics partagés ;
- un projet ou package **métier** séparé, qui référence les contrats ;
- dans le métier, des classes **Logic** pour les règles métier et l’orchestration des cas d’utilisation ;
- des **Operations** pour les accès aux données, séparées de la logique.

Adapter cette convention à HomeDocs plutôt que reproduire le couplage direct à MongoDB observé dans certaines applications MaNoir. La logique métier doit dépendre d’interfaces d’opérations de persistance, injectées et indépendantes du moteur. L’implémentation initiale utilise SQLite avec Entity Framework Core ; le `DbContext`, les migrations, les requêtes et les détails de stockage SQLite restent dans l’implémentation de persistance.

Les contrats et la logique métier ne doivent pas exposer de types ou de requêtes propres à SQLite, EF Core ou MongoDB. Concevoir les interfaces autour des opérations réellement nécessaires, sans construire de framework générique ni présumer qu’une requête propre à un moteur sera portable telle quelle. Prévoir des opérations simulées pour tester la logique indépendamment de la base et des tests d’intégration de l’implémentation SQLite.

Une future intégration à MaNoir pourrait utiliser la base MongoDB commune. La conception doit permettre d’ajouter une implémentation MongoDB des opérations sans réécrire les règles métier, mais **ne pas implémenter MongoDB ni choisir automatiquement ce moteur dans cette version**. Cette possibilité ne remplace pas le stockage des contenus Markdown et des médias défini ci-dessous.

# 4. Stockage

Utiliser un stockage hybride.

## 4.1 Fichiers Markdown

Conserver dans des fichiers Markdown :

- les pages ;
- les procédures ;
- les fiches d’équipement ;
- les fiches d’emplacement ;
- les pages liées aux pièces ;
- les procédures de dépannage ;
- la documentation technique.

Le Markdown doit rester lisible sous forme de fichier. Toute modification du contenu s’effectue exclusivement depuis le back-office administrateur ; la modification directe des fichiers en dehors de l’application n’est pas un mode d’édition pris en charge.

Le choix de l’emplacement et de l’organisation de ces fichiers dans le dépôt ou dans le volume persistant est laissé à ton appréciation.

Garantir toutefois :

- des identifiants stables ;
- des slugs stables ;
- une classification claire ;
- une validation des métadonnées ;
- une détection des doublons ;
- une prise en charge des brouillons ;
- un historique des versions ;
- une sauvegarde simple.

## 4.2 SQLite

Utiliser SQLite pour stocker :

- les utilisateurs ;
- les contextes d’utilisation ;
- les paramètres ;
- les connecteurs ;
- les secrets chiffrés ;
- les profils d’affichage ;
- les thèmes ;
- les écrans enregistrés ;
- les accès rapides ;
- les historiques de publication ;
- l’index de recherche si nécessaire ;
- les journaux d’exécution des actions ;
- la configuration MQTT ;
- les informations de sauvegarde ;
- les paramètres de sécurité.

L’accès à ces données passe par les interfaces d’opérations décrites en section 3.1. Les schémas et migrations SQLite sont propres à leur implémentation ; une future persistance MongoDB pourra avoir son propre modèle et ses propres mécanismes d’évolution.

## 4.3 Fichiers persistants

Conserver dans des volumes persistants :

- les images ;
- les PDF ;
- les documentations techniques ;
- les fichiers CSS ;
- les fichiers d’override de design des devices ;
- les thèmes ;
- les sauvegardes ;
- les clés de chiffrement ;
- la base SQLite ;
- les contenus Markdown.

Toutes les données persistantes doivent pouvoir être sauvegardées et restaurées simplement.

# 5. Structure fonctionnelle du front public

Créer les entrées principales suivantes :

- Rechercher
- Où est-ce ?
- Au quotidien
- Équipements
- Pièces
- J’ai un problème
- Documentation technique

# 6. Page d’accueil

Créer une page d’accueil adaptée à la racine du contexte actif. La page d’accueil de démonstration du manuel général peut comporter :

- le titre « Manuel de la maison » ;
- une grande barre de recherche immédiatement visible ;
- six grosses tuiles principales ;
- une zone « Accès rapides » ;
- une présentation adaptée au tactile ;
- une présentation adaptée à la télévision ;
- un focus clavier très visible.

Les tuiles principales proposées pour cette démonstration sont :

- Où est-ce ?
- Au quotidien
- Équipements
- Pièces
- J’ai un problème
- Documentation technique

Les accès rapides initiaux de démonstration sont :

- Wi-Fi invité
- Instructions de ménage
- Sortir les poubelles
- Couper l’eau
- Tableau électrique
- Four de la cuisine

Les accès rapides doivent être configurables depuis le back-office.

Ils ne doivent pas être codés en dur dans les composants.

Les accès rapides doivent pouvoir être différents selon :

- le contexte d’utilisation ;
- la page d’accueil de ce contexte.

La navigation et les accès rapides relèvent du contexte, pas du profil de device ni de son fichier d’override.

## 6.1 Contextes d’utilisation et arborescence

L’ensemble des contenus forme une arborescence commune. Un contexte d’utilisation définit une racine de navigation dans cette arborescence : cette racine est l’accueil du contexte et ne duplique pas les pages communes.

L’arborescence est constituée de pages et de sous-pages, elles-mêmes susceptibles de contenir d’autres sous-pages. L’administrateur la construit et la modifie dans le back-office. Il peut changer l’ordre des pages et sous-pages par glisser-déposer, avec une alternative accessible au clavier.

Une sous-page peut aussi être un lien vers une autre page ou sous-page de l’arborescence. Ce lien permet de faire apparaître une même fiche dans plusieurs parcours sans en dupliquer le contenu. La disponibilité par contexte et la protection restent celles de la page cible : le lien ne doit pas permettre de contourner ces règles. Le back-office doit signaler les liens invalides et empêcher les boucles de résolution de liens, sans interdire un simple lien vers une page parente.

Un seul contexte est actif à la fois. Il est fourni à l’invocation par l’appelant, notamment via l’URL. HomeDocs ne détermine pas lui-même le profil de la personne ni l’état de la maison : Home Assistant, Manoir ou un autre appelant choisit le contexte pertinent. Un contexte peut représenter par exemple un invité, le ménage, un habitant ou une situation de la maison.

L’administrateur peut créer et modifier les contextes depuis le back-office, notamment leur identifiant, leur nom et leur racine. Le contexte absent ou inconnu entraîne une réponse 404.

Chaque page porte une liste de contextes dans lesquels elle est disponible. Une liste vide signifie qu’elle est disponible dans tous les contextes. Une page disponible reste accessible par lien direct même si elle se trouve hors de la racine de navigation du contexte. Si une page n’est pas disponible dans le contexte invoqué, rediriger vers l’accueil de ce contexte.

La navigation, les liens et la recherche doivent respecter le contexte actif. La racine de navigation ne constitue pas à elle seule une restriction d’accès.

# 7. Recherche

Créer une recherche locale et rapide couvrant :

- les titres ;
- les résumés ;
- le contenu des pages ;
- les mots-clés ;
- les synonymes ;
- les références d’appareils ;
- les pièces ;
- les emplacements ;
- les noms alternatifs ;
- les métadonnées utiles.

La recherche doit afficher les résultats au fur et à mesure de la saisie et ne retourner que les pages disponibles dans le contexte actif.

Chaque résultat doit afficher :

- le titre ;
- la catégorie ;
- la pièce éventuelle ;
- un court extrait ;
- une icône ou un indicateur de type ;
- le chemin logique de la page.

Pour une page protégée mais disponible dans le contexte actif, afficher son titre, son résumé et une icône indiquant le verrouillage, même avant authentification. Ne pas exposer d’extrait du corps protégé avant autorisation.

Prévoir les synonymes courants.

Exemples :

- « sopalin » doit retrouver « essuie-tout » ;
- « pq » doit retrouver « papier toilette » ;
- « wifi » doit retrouver la procédure Internet ;
- « plaque » doit retrouver la plaque de cuisson ;
- « courant » doit retrouver la procédure de panne électrique.

La recherche doit fonctionner sur le réseau local sans dépendre d’un service externe.

# 8. Module « Où est-ce ? »

Créer une expérience spécifique permettant de rechercher un objet et d’afficher distinctement :

- où le trouver ;
- où le ranger ;
- où le jeter ou le recycler ;
- une photo éventuelle ;
- des remarques particulières.

Une fiche d’emplacement doit pouvoir contenir :

- nom ;
- noms alternatifs ;
- catégorie ;
- pièce ;
- chemin d’emplacement ;
- emplacement de rangement ;
- emplacement de rejet ou recyclage ;
- image ;
- notes ;
- mots-clés.

Exemple de rendu :

Titre : Ampoules

Où trouver les ampoules neuves :

Buanderie → Armoire → Étagère du haut → Bac « Électricité »

Où déposer les ampoules usagées :

Garage → Zone recyclage → Bac spécial

Créer plusieurs données de démonstration réalistes :

- ampoules ;
- piles ;
- sacs poubelles ;
- essuie-tout ;
- produits ménagers ;
- aspirateur ;
- rallonge électrique ;
- serviettes pour les invités.

Les données de démonstration doivent être clairement identifiables et faciles à remplacer.

# 9. Procédures du quotidien

Créer un type de contenu dédié aux procédures.

Une procédure doit pouvoir contenir :

- titre ;
- résumé ;
- catégorie ;
- pièce éventuelle ;
- public concerné ;
- durée estimée ;
- difficulté ;
- matériel nécessaire ;
- étapes numérotées ;
- avertissements ;
- interdictions ;
- photos ;
- date de dernière vérification ;
- liens associés ;
- actions domotiques associées ;
- états domotiques affichés.

Créer des exemples pour :

- instructions générales de ménage ;
- ménage de la cuisine ;
- sortir les poubelles ;
- préparer la maison avant un départ prolongé ;
- accueillir des invités ;
- redémarrer la télévision de la cuisine.

Les avertissements importants doivent être très visibles.

Exemple :

« Ne pas utiliser de produit anticalcaire sur le plan de travail. »

# 10. Fiches d’équipements

Créer un type de contenu dédié aux équipements.

Une fiche d’équipement doit pouvoir contenir :

- nom ;
- type ;
- pièce ;
- marque ;
- modèle ;
- référence ;
- numéro de série facultatif ;
- date d’achat facultative ;
- date de fin de garantie facultative ;
- photo ;
- résumé ;
- usages courants ;
- réglages favoris de la maison ;
- nettoyage ;
- entretien ;
- dépannage ;
- codes d’erreur ;
- coordonnées du SAV ;
- lien vers un manuel PDF local ;
- autres documents associés ;
- états domotiques ;
- actions domotiques disponibles dans le contexte.

Créer des exemples pour :

- four de la cuisine ;
- plaque de cuisson ;
- lave-vaisselle ;
- hotte ;
- réfrigérateur.

Pour le four, créer notamment des sections de démonstration :

- chaleur tournante ;
- cuisson d’une pizza ;
- préchauffage ;
- pyrolyse ;
- nettoyage ;
- erreurs fréquentes.

Ne pas inventer de véritables caractéristiques constructeur.

Utiliser clairement des contenus factices destinés à être remplacés.

# 11. Navigation par pièce

Créer une vue présentant les pièces sous forme de grandes cartes.

Créer les pièces de démonstration suivantes :

- Cuisine
- Salon
- Buanderie
- Salle de bain
- Chambre
- Garage
- Extérieur

Une page de pièce doit réunir automatiquement :

- les équipements associés ;
- les procédures associées ;
- les objets et emplacements associés ;
- les accès rapides associés ;
- les procédures de dépannage associées ;
- les états domotiques associés ;
- les actions domotiques associées.

Prévoir ultérieurement l’intégration d’un plan simplifié de la maison.

Ne pas créer pour l’instant de moteur de plan interactif complexe.

Prévoir dans l’architecture fonctionnelle la possibilité d’ajouter plus tard :

- une image du plan ;
- un plan SVG cliquable ;
- une représentation provenant de Manoir.

# 12. Rubrique « J’ai un problème »

Créer une page avec de très grosses tuiles :

- Plus d’électricité
- Fuite d’eau
- Chauffage
- Plus d’Internet
- Domotique en panne
- Porte ou portail
- Appareil de cuisine
- Qui appeler ?

Créer des procédures de démonstration simples, destinées à une personne non technique.

Limiter les premières actions à des vérifications sans danger.

Pour les actions potentiellement dangereuses, afficher une consigne explicite invitant à arrêter la procédure et à contacter une personne compétente.

Ne jamais présenter une intervention électrique, gaz ou plomberie à risque comme une procédure banale.

Chaque procédure de panne doit pouvoir contenir :

- symptômes ;
- vérifications simples ;
- étapes ;
- états domotiques utiles ;
- conditions d’arrêt ;
- personne à contacter ;
- informations à transmettre ;
- liens vers la documentation technique associée ;
- actions domotiques disponibles dans le contexte.

# 13. Documentation technique

Créer une section séparée pour la documentation avancée.

Prévoir les catégories suivantes :

- Home Assistant
- Réseau
- Wi-Fi et UniFi
- Zigbee
- ESPHome
- Tasmota
- Shelly
- Serveurs
- Sauvegardes
- Tableau électrique
- Eau et chauffage
- Procédures de restauration

Créer seulement quelques pages de démonstration.

Ne pas inventer l’infrastructure réelle de la maison.

Ajouter sur chaque page technique un encart indiquant :

« Documentation technique. Ces informations sont destinées aux personnes connaissant l’installation. »

Prévoir la possibilité :

- de masquer cette rubrique ;
- de limiter sa disponibilité à certains contextes ;
- de protéger certaines pages ;
- de masquer certaines informations sensibles.

# 14. Mode tablette et mode télévision

L’application doit prendre correctement en charge :

- le tactile ;
- la souris ;
- le clavier ;
- une télécommande se comportant comme un clavier directionnel.

Implémenter une navigation complète avec :

- flèche haut ;
- flèche bas ;
- flèche gauche ;
- flèche droite ;
- Entrée ;
- Échap ou Retour.

Le focus clavier doit toujours être clairement visible.

Règles d’interface TV :

- grandes zones cliquables ;
- taille de police confortable ;
- contrastes élevés ;
- nombre limité d’actions simultanément visibles ;
- aucun fonctionnement dépendant du survol ;
- aucun geste tactile obligatoire ;
- aucun menu minuscule ;
- aucun contenu important uniquement dans une infobulle ;
- défilement simple et prévisible ;
- retour simple à la page précédente ;
- retour automatique possible à la racine du contexte actif après inactivité, sans changer de contexte.

# 15. Profils d’affichage

Créer un système de profils d’affichage, aussi appelés profils de device. Un profil détermine la présentation et la manière de piloter l’interface ; la navigation dans les contenus et sa racine relèvent du contexte d’utilisation.

Créer initialement les profils suivants :

- desktop ;
- mobile ;
- tablet-wall ;
- tablet-dock ;
- tv.

Un profil doit pouvoir définir :

- thème ;
- taille de police ;
- facteur d’échelle typographique ;
- facteur d’échelle des espacements ;
- nombre maximal de colonnes ;
- taille des cartes ;
- mode d’interaction et de pilotage de l’interface ;
- taille minimale des zones interactives ;
- largeur maximale du contenu ;
- affichage ou non des actions tactiles ;
- affichage ou non d’éléments de présentation ;
- CSS personnalisé facultatif.

Le profil est fourni par l’appelant à l’invocation, par exemple dans l’URL, distinctement du contexte et de l’identifiant facultatif d’un device. Si le profil est absent ou inconnu, répondre 404 : aucune détection automatique ni préférence locale ne doit le remplacer silencieusement.

Exemples illustratifs, dont la syntaxe exacte reste à définir :

/?context=invites&profile=tablet-wall

/?context=menage&profile=tv&device=fire-tv-cuisine

/equipements/four-cuisine?context=habitant&profile=desktop

# 16. Écrans enregistrés

Permettre d’enregistrer des écrans tels que :

- tablette murale ;
- Pixel Tablet ;
- Fire TV cuisine.

Un écran enregistré, ou device, est facultatif dans l’invocation. Il est distinct du profil générique fourni par l’appelant et du contexte actif.

Chaque écran doit posséder :

- un identifiant stable ;
- un nom ;
- éventuellement un fichier d’override de design ;
- un statut actif ou inactif ;
- une date de dernière connexion ;
- éventuellement une clé d’enregistrement.

L’administrateur gère dans le back-office le fichier d’override propre à chaque device. Cet override ne concerne que le design ; il ne définit ni racine de navigation, ni contexte, ni permission d’action.

Tous les réglages de design du profil doivent pouvoir être surchargés pour un device, y compris par l’ajout de CSS supplémentaire. L’override complète ou remplace la présentation héritée sans modifier le profil de référence ni les règles fonctionnelles du contexte.

Les liens associés aux écrans doivent conserver un profil et un contexte explicites, par exemple :

/?context=invites&profile=tablet-wall&device=tablette-entree

/?context=habitant&profile=tv&device=fire-tv-cuisine

L’administration doit pouvoir afficher les écrans actuellement connectés grâce à SignalR.

# 17. Design et thèmes

Créer un design chaleureux, domestique et moderne.

Utiliser :

- une palette chaude et calme ;
- de grandes cartes ;
- des coins légèrement arrondis ;
- une typographie très lisible ;
- des icônes cohérentes ;
- des espacements généreux ;
- des retours visuels clairs lors de la sélection.

Prévoir :

- un thème clair ;
- un thème sombre ;
- le respect des préférences système ;
- la possibilité de forcer un thème ;
- plusieurs thèmes personnalisables.

# 18. Gestion des CSS

Créer dans le back-office deux niveaux d’édition.

## 18.1 Éditeur visuel

Permettre de modifier :

- couleurs ;
- polices ;
- taille du texte ;
- espacements ;
- rayon des cartes ;
- ombres ;
- taille minimale des boutons ;
- nombre de colonnes ;
- largeur maximale du contenu ;
- thème clair ou sombre ;
- apparence du focus ;
- taille des icônes.

Afficher un aperçu en direct.

## 18.2 Éditeur CSS avancé

Permettre de gérer :

- CSS commun ;
- CSS par thème ;
- CSS par profil ;
- CSS personnalisé par écran si nécessaire.

Ajouter :

- coloration syntaxique ;
- validation CSS ;
- historique des versions ;
- restauration d’une version ;
- aperçu avant publication ;
- possibilité de désactiver temporairement une surcharge CSS incorrecte.

Appliquer les styles dans cet ordre :

1. styles système ;
2. thème sélectionné ;
3. profil d’affichage ;
4. CSS personnalisé ;
5. éventuelle surcharge propre à l’écran.

Ne pas utiliser un fichier CSS complètement indépendant pour chaque modèle d’appareil si un système de variables et de profils suffit.

Ne pas imposer une organisation particulière des fichiers CSS. Choisir une organisation cohérente avec l’architecture générale retenue.

# 19. Accessibilité

Respecter autant que possible WCAG 2.2 niveau AA.

Implémenter notamment :

- HTML sémantique ;
- navigation intégrale au clavier ;
- focus visible ;
- contrastes suffisants ;
- tailles tactiles généreuses ;
- textes alternatifs pour les images ;
- libellés accessibles ;
- lien d’évitement vers le contenu ;
- respect de prefers-reduced-motion ;
- absence d’information transmise uniquement par la couleur ;
- annonces accessibles pour les résultats d’actions ;
- gestion correcte du focus dans les dialogues de confirmation.

# 20. Back-office d’administration

Créer un back-office authentifié.

L’administration doit toujours nécessiter une connexion.

L’interface du back-office doit être multilingue, en français et en anglais. Les libellés, menus, messages, validations et autres textes propres à l’administration doivent être traduits dans ces deux langues. Par défaut, utiliser la langue du navigateur de l’administrateur lorsqu’elle est prise en charge ; prévoir un repli explicite vers une langue disponible si elle ne l’est pas.

L’administrateur peut choisir sa langue dans ses préférences d’interface ; ce choix est conservé et prévaut sur la langue du navigateur.

Cette exigence concerne l’interface d’administration uniquement. Les contenus du manuel ne sont pas gérés en plusieurs langues : ne pas imposer de traduction, de variantes linguistiques ni de modèle multilingue pour les pages, les médias ou les autres contenus éditoriaux.

Créer les rubriques suivantes :

- Tableau de bord
- Contenus
- Médias
- Pièces
- Équipements
- Emplacements
- Procédures
- Dépannage
- Documentation technique
- Contextes d’utilisation
- Connecteurs
- Home Assistant
- MQTT
- Thèmes
- Profils d’affichage
- Écrans
- Utilisateurs
- Sauvegardes
- Journaux
- Paramètres

## 20.1 Pages de paramètres

Organiser les paramètres en pages distinctes :

- **Préférences UI** : notamment le choix et la conservation de la langue de l’interface d’administration.
- **Sécurité** : définition et gestion du compte administrateur, de son mot de passe et du PIN commun utilisé pour les pages et actions protégées.
- **Intégration Home Assistant** : configuration de l’URL et du token d’accès longue durée, et test de connexion.
- **Autres intégrations** : prévoir l’ajout de pages de configuration pour les futures intégrations, notamment Manoir, sans les implémenter dans cette version.

Ces paramètres sont accessibles uniquement dans le back-office authentifié. Les mots de passe, le PIN et les tokens ne doivent pas être affichés en clair ni journalisés.

# 21. Tableau de bord d’administration

Afficher notamment :

- statut de l’application ;
- version ;
- état de la connexion Home Assistant ;
- état de la connexion MQTT ;
- nombre de pages ;
- nombre de brouillons ;
- erreurs Markdown ;
- actions invalides ;
- écrans actuellement connectés ;
- date de dernière publication ;
- date de dernière sauvegarde ;
- éventuels problèmes de configuration.

# 22. Gestion du contenu

Permettre :

- de lister les pages ;
- de créer une page ;
- de modifier une page ;
- de prévisualiser une page ;
- de publier une page ;
- de remettre une page en brouillon ;
- de supprimer une page ;
- de dupliquer une page ;
- de modifier les métadonnées ;
- de classer les pages par pièce et catégorie ;
- de créer des pages et sous-pages dans une arborescence commune ;
- de créer une sous-page qui est un lien vers une autre page ou sous-page ;
- de réordonner les pages et sous-pages par glisser-déposer ;
- de rechercher dans les contenus ;
- de vérifier les liens ;
- d’afficher les liens entrants ;
- de vérifier les directives domotiques ;
- de détecter les blocs d’action invalides ou les cibles inconnues ;
- de conserver un historique des modifications ;
- de restaurer une version précédente.

# 23. Éditeur Markdown

Créer un véritable éditeur Markdown intégré au back-office.

L’éditeur doit proposer :

- coloration syntaxique ;
- numéros de ligne si pertinent ;
- aperçu en direct ;
- métadonnées structurées ;
- insertion d’images ;
- insertion de documents ;
- gestion intelligente des médias intégrée à l’édition : choisir ou importer un média, l’insérer dans le Markdown et retrouver ses utilisations sans gérer manuellement les chemins ;
- insertion assistée de directives domotiques ;
- recherche d’une entité Home Assistant ;
- sélection d’un type d’action du catalogue intégré ;
- validation du Markdown ;
- validation des directives ;
- détection des références inexistantes ;
- détection des identifiants dupliqués ;
- indication claire des erreurs avant publication.

Le Markdown produit doit rester lisible ; son édition s’effectue dans le back-office.

Le détail des interactions de la gestion intelligente des médias reste à préciser ; elle s’appuie sur le gestionnaire de médias de la section 40.

# 24. Modèles de contenu

Créer des schémas de métadonnées validés pour :

- pièces ;
- emplacements ;
- procédures ;
- équipements ;
- dépannage ;
- documentation technique.

Les métadonnées peuvent utiliser un front matter YAML ou une autre représentation textuelle standard, lisible et adaptée au Markdown.

Chaque page doit notamment permettre de définir ses contextes de disponibilité et son éventuelle protection par authentification.

Créer des composants réutilisables pour :

- carte de navigation ;
- résultat de recherche ;
- fil d’Ariane ;
- avertissement ;
- interdiction ;
- étapes d’une procédure ;
- fiche d’emplacement ;
- fiche d’équipement ;
- contact ;
- document lié ;
- métadonnées de page ;
- accès rapide ;
- état domotique ;
- bouton d’action domotique ;
- groupe d’actions ;
- résultat d’exécution.

L’organisation et l’emplacement de ces composants sont laissés à ton appréciation.

# 25. Abstraction des connecteurs domotiques

Créer une abstraction générique de connecteur domotique.

Cette abstraction doit permettre ultérieurement d’ajouter :

- Gladys ;
- Manoir.

Ne pas implémenter Gladys ni Manoir dans cette version.

L’abstraction doit au minimum permettre :

- de tester la connexion ;
- de récupérer les entités ;
- de rechercher les entités ;
- de récupérer l’état d’une entité ;
- de récupérer les actions ou services disponibles ;
- d’exécuter une action ;
- de vérifier la disponibilité du connecteur ;
- d’obtenir les capacités du connecteur.

Implémenter uniquement Home Assistant pour la première version.

Les concepts spécifiques à Home Assistant ne doivent pas être disséminés dans tout le domaine ou dans le moteur Markdown.

# 26. Connecteur Home Assistant

Permettre de configurer :

- nom du connecteur ;
- URL de Home Assistant ;
- jeton d’accès longue durée Home Assistant ;
- validation TLS ;
- timeout ;
- mode lecture seule ;
- autorisation ou non d’exécuter des actions ;
- capacités nécessaires aux types d’actions intégrés.

Le jeton doit :

- être chiffré au repos ;
- ne jamais apparaître dans les journaux ;
- ne jamais être renvoyé au navigateur après son enregistrement ;
- ne jamais être intégré dans le Markdown ;
- ne jamais être exposé au front public.

Créer une interface d’administration permettant :

- de tester la connexion ;
- de voir l’état de la connexion ;
- de parcourir les entités ;
- de rechercher une entité ;
- de voir l’état d’une entité ;
- de voir les attributs utiles ;
- de parcourir les domaines et services ;
- d’aider l’administrateur à insérer un bloc d’action dans une page depuis une entité.

Tous les appels vers Home Assistant doivent passer par l’API serveur.

Le serveur utilise l’URL et le jeton longue durée enregistrés dans la configuration pour appeler l’API Home Assistant. Ce jeton de connexion à Home Assistant est distinct du token d’authentification transmis par un appelant pour accéder aux éléments protégés de HomeDocs.

# 27. Catalogue intégré d’actions domotiques

Pour cette version, ne pas créer de registre ni de catalogue d’actions personnalisable. Proposer un mini-catalogue intégré de types d’actions Home Assistant :

- switch : allumer, éteindre ou basculer ;
- scène : activer ou désactiver ;
- entité : définir une valeur fixée dans le bloc ;
- automatisation : lancer ;
- script : lancer.

Vérifier lors de la conception détaillée les capacités réellement proposées par Home Assistant, notamment pour la désactivation d’une scène et la définition d’une valeur selon le type d’entité. Ne pas prétendre qu’une opération non prise en charge a réussi.

Une action est définie dans un bloc Markdown d’une page par :

- l’intégration ciblée : Home Assistant ou, ultérieurement, Manoir ;
- le type d’action : switch, scène, entité, automatisation ou script ;
- l’action à effectuer, par exemple allumer, éteindre ou lancer ;
- la cible ;
- une valeur fixe, uniquement si l’action en nécessite une ;
- les données de présentation locales : texte affiché, logo et classe CSS spécifique, selon les besoins ;
- une liste de contextes autorisés ;
- une éventuelle protection par authentification.

La valeur ne sera probablement pas nécessaire pour la plupart des actions actuelles ; elle reste disponible pour les opérations qui en ont besoin, notamment la définition d’une valeur d’entité. Seule l’intégration Home Assistant est implémentée dans cette version ; la syntaxe doit néanmoins permettre de désigner Manoir plus tard, sans créer de fausse intégration fonctionnelle.

Ces données de présentation sont propres au bloc et ne sont pas transmises à l’intégration domotique. Le logo et la classe CSS sont facultatifs ; leur utilisation ne doit pas modifier les règles de disponibilité ou de protection de l’action.

Une liste de contextes vide rend l’action disponible dans tous les contextes, sous réserve que la page elle-même soit disponible.

Une action non disponible dans le contexte actif reste visible, mais grisée et désactivée. Ne pas ajouter de restrictions fonctionnelles par écran, profil, horaire, niveau de risque ou fréquence, ni de confirmation obligatoire : seules les règles de contexte et de protection définies ici s’appliquent.

Journaliser les exécutions et les refus sans informations sensibles, notamment avec la date, la page et le bloc concernés, le contexte, le résultat, la durée et une éventuelle erreur.

# 28. Sécurité des pages et des actions

La majorité des contenus est consultable sans authentification. Une page ou une action peut cependant être marquée comme protégée. L’accès à un élément protégé nécessite soit une authentification valide transmise par l’appelant, notamment Home Assistant ou Manoir, soit la saisie locale d’un PIN commun.

L’autorisation obtenue vaut pour les pages et actions protégées disponibles dans le contexte actif. Elle n’ouvre pas l’accès aux éléments indisponibles dans ce contexte. Elle prend fin lors d’un changement de contexte ou après 30 secondes sans interaction.

À l’expiration, présenter la saisie du PIN et un bouton de retour à la racine du contexte, dont les contenus non protégés restent consultables. Une page indisponible dans le contexte redirige vers cette racine, indépendamment de sa protection.

Le nom du contexte dans l’URL n’est pas une preuve d’authentification. Pour transmettre une authentification, les appelants utilisent un token signé de type JWT, avec le mécanisme le plus simple possible. HomeDocs doit vérifier la signature et la validité du token, ainsi que l’identité de l’émetteur autorisé et le destinataire prévu, avant d’accorder l’autorisation temporaire. Le format précis, la transmission et la configuration de confiance seront définis à l’implémentation ; cette intégration n’est pas prioritaire et la protection par PIN peut être réalisée d’abord. Ne pas placer le token ni aucun autre secret dans l’URL. Le PIN ne doit jamais être conservé ni journalisé en clair.

Les appels domotiques sont exécutés côté serveur. Celui-ci vérifie le contexte, la disponibilité de la page et du bloc publié, ainsi que l’autorisation temporaire pour les éléments protégés. Il ne fait pas confiance à une cible, une opération ou une valeur arbitraire transmise par le navigateur : il utilise la définition du bloc publiée par l’administrateur et valide le type d’action pris en charge.

Toutes les modifications de contenu et de blocs d’action nécessitent une connexion au back-office administrateur.

# 29. Markdown enrichi domotique

Implémenter une extension Markdown avec des directives spécialisées.

Le parseur ne doit jamais exécuter de code provenant du Markdown.

Les directives doivent être transformées en composants d’interface sûrs.

Les blocs d’action définissent directement les paramètres autorisés par le mini-catalogue intégré. Leur syntaxe exacte reste à préciser ; les exemples ci-dessous sont illustratifs.

Les actions utilisent une syntaxe de bloc délimité, comparable aux blocs de code Markdown, et non une simple directive sur une ligne. Le contenu du bloc est déclaratif : il décrit l’action et sa présentation, sans contenir de code à exécuter.

Cette syntaxe est destinée à évoluer dans les versions futures. Prévoir une structure extensible, une validation explicite et une stratégie de compatibilité ou de migration documentée afin de ne pas casser silencieusement les pages existantes. Un bloc inconnu, invalide ou non pris en charge doit produire une erreur explicite.

## 29.1 Afficher un état Home Assistant

Exemple :

::ha-state{entity="sensor.cuisine_temperature" label="Température de la cuisine"}

## 29.2 Afficher une action domotique

Exemple :

```automation-action
integration: home-assistant
type: switch
action: "off"
target: switch.cuisine
label: Éteindre
logo: lumiere
class: action-cuisine
contexts: [habitant, menage]
protected: false
```

L’intégration, le type, l’action, la cible, les données de présentation, les contextes et la protection sont définis dans le bloc. Une action nécessitant une valeur contient également la valeur fixe à envoyer. Les noms exacts des directives et attributs, ainsi que la façon de référencer un logo, restent à valider.

## 29.3 Action protégée

Exemple :

```automation-action
integration: home-assistant
type: script
action: run
target: script.exemple
label: Lancer
contexts: [habitant]
protected: true
```

L’action exige une autorisation temporaire valide dans le contexte actif, même si sa page est consultable sans authentification.

## 29.4 Carte d’entité

Exemple :

:::ha-entity{entity="climate.cuisine" title="Chauffage de la cuisine"}
Affiche la température, la consigne et le mode actuel.
:::

## 29.5 Groupe d’actions

Exemple :

:::automation-panel{title="Éclairage de la cuisine"}
```automation-action
integration: home-assistant
type: switch
action: "on"
target: switch.cuisine
label: Allumer
```

```automation-action
integration: home-assistant
type: switch
action: "off"
target: switch.cuisine
label: Éteindre
```

```automation-action
integration: home-assistant
type: scene
action: activate
target: scene.soir
label: Ambiance du soir
```
:::

## 29.6 Affichage conditionnel

Exemple :

:::ha-visible-when{entity="binary_sensor.internet_available" state="off"}
Internet semble indisponible. Suivez la procédure ci-dessous.
:::

Limiter l’expression conditionnelle à une syntaxe sûre et déclarative.

Ne pas permettre l’exécution de :

- JavaScript ;
- C# ;
- templates arbitraires ;
- scripts shell ;
- expressions non contrôlées.

Créer une erreur explicite dans le back-office lorsqu’une directive est :

- invalide ;
- inconnue ;
- incomplète ;
- liée à une entité inconnue ;
- liée à un type ou une opération non pris en charge ;
- liée à un contexte inexistant.

# 30. Rendu en temps réel des états

Utiliser SignalR ou un mécanisme approprié pour actualiser les états domotiques affichés.

Prévoir :

- une gestion des déconnexions ;
- une reconnexion automatique ;
- un indicateur d’état périmé ;
- un rafraîchissement périodique raisonnable ;
- une limitation du nombre d’appels à Home Assistant ;
- un cache court côté serveur ;
- un retour lisible lorsque Home Assistant est indisponible.

Le front public ne doit pas contacter Home Assistant directement.

# 31. MQTT

Dans un premier jet, MQTT sert avant tout à annoncer la présence et la disponibilité de HomeDocs sur le réseau local. Prévoir une publication de présence identifiable et stable, avec un état en ligne / hors ligne, et une reconnexion au broker.

Implémenter progressivement un connecteur MQTT configurable avec :

- hôte ;
- port ;
- TLS ;
- utilisateur ;
- mot de passe ;
- client ID ;
- préfixe de base ;
- préfixe de découverte Home Assistant ;
- keep-alive ;
- reconnexion automatique ;
- Last Will and Testament ;
- qualité de service configurable si nécessaire.

Les secrets MQTT doivent être :

- chiffrés au repos ;
- masqués dans les journaux ;
- non renvoyés au navigateur après enregistrement ;
- utilisés uniquement côté serveur.

# 32. MQTT Discovery pour Home Assistant

MQTT Discovery et les entités détaillées ci-dessous restent dans le périmètre global, mais ne sont pas requis pour le premier jet limité à la présence. Ils seront introduits progressivement ; leur périmètre et leur ordre de réalisation restent à préciser.

Publier l’application comme un appareil Home Assistant appelé par défaut :

- Nom : Manuel de la maison
- Fabricant : MaNoir
- Modèle : House Manual

Rendre ces informations configurables.

Prévoir les entités suivantes pour une étape ultérieure :

- sensor.house_manual_status
- sensor.house_manual_version
- sensor.house_manual_last_publish
- sensor.house_manual_connected_displays
- binary_sensor.house_manual_home_assistant_connection
- binary_sensor.house_manual_mqtt_connection
- button.house_manual_reload_content
- button.house_manual_clear_cache
- button.house_manual_refresh_displays

Il est possible d’ajouter une entité select pour le thème par défaut si cela reste simple et cohérent.

Utiliser des identifiants uniques stables.

Ne jamais générer d’identifiant aléatoire différent à chaque démarrage.

Publier les configurations MQTT Discovery avec retain=true.

Publier les états persistants appropriés avec retain=true.

Publier les commandes ponctuelles et événements avec retain=false.

Publier un état de disponibilité :

- online au démarrage et après connexion ;
- offline lors d’un arrêt normal ;
- offline via Last Will après une coupure brutale.

Au retrait d’une entité, publier un payload vide sur son topic de configuration afin de ne pas laisser une entité orpheline dans Home Assistant.

Le préfixe de découverte doit être configurable.

# 33. Organisation logique des topics MQTT

Utiliser une convention de topics cohérente. Le premier jet couvre au minimum la présence et la disponibilité ; les autres catégories ci-dessous correspondent aux évolutions prévues :

- les configurations MQTT Discovery ;
- les états ;
- la disponibilité ;
- les commandes ;
- les événements ;
- les informations propres à chaque écran.

La convention choisie doit :

- utiliser des identifiants stables ;
- être configurable ;
- être documentée ;
- éviter les collisions ;
- permettre le nettoyage d’anciennes entités ;
- permettre ensuite de fonctionner correctement avec MQTT Discovery de Home Assistant.

Il n’est pas imposé d’arborescence exacte pour les topics.

Prévoir à terme dans le back-office un aperçu des topics et payloads générés avant publication.

# 34. Administration MQTT

Créer progressivement une page d’administration permettant d’abord de configurer et tester le broker et de voir l’état de connexion. Prévoir ensuite :

- de configurer le broker ;
- de tester la connexion ;
- d’afficher l’état de connexion ;
- d’afficher les configurations MQTT Discovery générées ;
- de republier la découverte ;
- de retirer une entité ;
- de nettoyer les anciennes configurations ;
- de consulter les derniers messages publiés ;
- de consulter les derniers messages reçus ;
- de tester une publication ;
- de voir la date de dernière connexion ;
- de voir les erreurs de reconnexion.

Ne pas afficher les mots de passe.

# 35. Actions MQTT dans le contenu

Les actions déclenchées depuis les pages sont limitées pour cette version au mini-catalogue Home Assistant décrit en section 27. Ne pas créer de registre d’actions MQTT ni de directive Markdown permettant de publier arbitrairement sur MQTT.

Le connecteur MQTT et les évolutions MQTT Discovery décrits dans les sections précédentes restent dans le périmètre, avec une première étape centrée sur la présence.

# 36. Authentification et autorisations

Implémenter une authentification locale simple pour le back-office administrateur et la protection temporaire des éléments du front décrite en section 28.

## Administrateur

L’administrateur dispose notamment des droits suivants :

- accès complet ;
- gestion des utilisateurs ;
- gestion des connecteurs ;
- gestion des secrets ;
- gestion MQTT ;
- gestion des actions ;
- gestion des sauvegardes ;
- gestion des thèmes et CSS ;
- gestion des paramètres.

Toutes les modifications de contenu et de configuration passent par l’administration ; ne pas créer de rôle éditeur distinct dans cette version.

L’administration doit toujours nécessiter une connexion.

Le front est consultable sans connexion pour les pages non protégées disponibles dans le contexte actif. Les pages et actions protégées exigent une authentification valide ou le PIN commun, selon les règles de la section 28.

Prévoir une architecture permettant d’ajouter ultérieurement OpenID Connect.

Ne pas implémenter OpenID Connect dans cette version.

# 37. API

Créer une API documentée avec OpenAPI.

Séparer clairement les responsabilités et points d’accès liés à :

- contenu ;
- médias ;
- recherche ;
- affichage ;
- administration ;
- utilisateurs ;
- profils ;
- contextes d’utilisation ;
- thèmes ;
- écrans ;
- connecteurs ;
- Home Assistant ;
- actions ;
- MQTT ;
- sauvegardes ;
- journaux.

L’organisation exacte des contrôleurs, endpoints ou groupes de routes est laissée à ton appréciation.

Ajouter :

- validation des entrées ;
- gestion uniforme des erreurs ;
- réponses ProblemDetails ;
- journalisation structurée ;
- corrélation des requêtes ;
- contrôles d’autorisation côté serveur ;
- protection contre les appels non autorisés.

# 38. URLs publiques

Créer des URLs publiques simples, stables et explicites. Chaque invocation doit fournir un contexte et un profil valides ; l’identifiant d’un device est facultatif. Préserver le contexte et le profil dans les liens, les redirections et la navigation.

Exemples illustratifs, dont la syntaxe exacte reste à définir :

/ou-est-ce/?context=invites&profile=mobile

/ou-est-ce/ampoules/?context=menage&profile=tablet-wall

/procedures/menage-cuisine/?context=menage&profile=tablet-wall

/equipements/four-cuisine/?context=habitant&profile=desktop

/pieces/cuisine/?context=invites&profile=mobile

/problemes/plus-internet/?context=habitant&profile=tv&device=fire-tv-cuisine

/technique/home-assistant/?context=habitant&profile=desktop

Ces URLs seront utilisées dans :

- Home Assistant ;
- des QR codes ;
- les favoris des tablettes ;
- les raccourcis de la Fire TV.

Les URLs ne doivent pas dépendre d’identifiants aléatoires.

Créer un mécanisme de redirection lorsqu’un slug publié est modifié afin d’éviter de casser les QR codes existants.

# 39. Hébergement local et disponibilité

L’application est hébergée sur le réseau local, dans un homelab, par exemple à côté de Home Assistant. Elle doit fonctionner sans accès Internet tant que le serveur HomeDocs et les services locaux nécessaires sont disponibles.

La consultation lorsque le serveur HomeDocs lui-même est inaccessible n’est pas une exigence de la première étape. Une PWA ou un cache de contenus consultables hors connexion pourra être étudié ultérieurement, sans le confondre avec le fonctionnement local sans Internet. Un éventuel cache ne doit jamais permettre de consulter du contenu protégé sans autorisation ou après son expiration.

Ne pas prétendre qu’une action domotique a réussi lorsque la connexion au serveur ou à Home Assistant est indisponible.

Les actions doivent être désactivées ou afficher une erreur claire lorsque le serveur HomeDocs ou Home Assistant est indisponible.

Le site doit rester utilisable sur le réseau local sans accès Internet.

# 40. Gestion des médias

Créer un gestionnaire de médias permettant :

- d’importer des images ;
- d’importer des PDF ;
- de renommer un média ;
- d’ajouter un titre ;
- d’ajouter un texte alternatif ;
- de classer les médias ;
- de rechercher les médias ;
- de voir où un média est utilisé ;
- de supprimer un média inutilisé ;
- d’empêcher ou d’avertir avant la suppression d’un média utilisé.

Les médias doivent être conservés dans un stockage local persistant.

Ne pas dépendre d’un stockage cloud.

L’organisation physique des médias est laissée à ton appréciation.

# 41. Sauvegardes

Créer une fonction de sauvegarde produisant une archive contenant :

- base SQLite ;
- fichiers Markdown ;
- images ;
- PDF ;
- thèmes ;
- CSS ;
- fichiers d’override des devices ;
- paramètres non secrets ;
- secrets chiffrés si une clé de restauration est disponible ;
- manifeste de sauvegarde ;
- version de l’application et du schéma.

Créer une procédure documentée de restauration.

Conserver automatiquement plusieurs sauvegardes avec une politique de rétention configurable.

Le back-office doit permettre :

- de lancer une sauvegarde ;
- de télécharger une sauvegarde ;
- de voir les sauvegardes disponibles ;
- de supprimer une ancienne sauvegarde ;
- de vérifier une archive ;
- de restaurer une sauvegarde après confirmation explicite.

# 42. Journalisation

Créer une journalisation structurée.

Journaliser notamment :

- démarrage de l’application ;
- erreurs de parsing Markdown ;
- publication d’un contenu ;
- modification d’un thème ;
- modification d’un CSS ;
- connexions et déconnexions de Home Assistant ;
- connexions et déconnexions MQTT ;
- publications MQTT importantes ;
- exécutions d’actions ;
- refus d’actions ;
- sauvegardes ;
- restaurations ;
- erreurs d’administration.

Ne jamais journaliser :

- mots de passe ;
- jetons Home Assistant ;
- mots de passe MQTT ;
- PIN en clair ;
- cookies d’authentification ;
- secrets chiffrés ou déchiffrés.

# 43. Déploiement Docker

Créer une configuration permettant de construire et lancer l’application avec Docker Compose.

Prévoir :

- une construction multi-stage ;
- des volumes persistants ;
- un healthcheck ;
- une politique restart unless-stopped ;
- des journaux structurés ;
- des variables d’environnement documentées ;
- un exemple de configuration sans secret réel.

Prévoir la persistance de :

- la base de données ;
- les contenus ;
- les médias ;
- les thèmes ;
- les CSS ;
- les sauvegardes ;
- les clés de protection des données.

Le projet doit pouvoir démarrer avec :

docker compose up -d --build

Exposer par défaut l’application sur le port 8080 de l’hôte.

Prévoir une variable permettant de modifier ce port.

Ne pas placer de secret réel dans le dépôt.

Choisir librement les noms et l’organisation des fichiers liés à Docker, tant que la commande précédente fonctionne et que la configuration est documentée.

# 44. Compte administrateur initial

Prévoir une page d’onboarding permettant de créer le premier compte administrateur lorsque l’application n’a pas encore de compte. Après cette création, l’onboarding initial ne doit plus permettre la création non authentifiée d’un autre compte.

Ne pas fournir de mot de passe par défaut permanent.

Documenter clairement la procédure.

En version ultérieure, envisager la détection d’autres applications MaNoir afin de réutiliser un compte existant. Cette évolution dépend notamment de la future intégration MaNoir : ne pas l’implémenter dans la première version ni supposer qu’elle est disponible.

# 45. Documentation du projet

Créer une documentation complète en français expliquant :

- l’objectif du projet ;
- l’architecture retenue ;
- les principaux choix techniques ;
- les prérequis ;
- le lancement en développement ;
- la génération du front ;
- le lancement avec Docker ;
- la création du premier administrateur ;
- l’organisation des contenus ;
- l’ajout d’une pièce ;
- l’ajout d’un équipement ;
- l’ajout d’un emplacement ;
- l’ajout d’une procédure ;
- l’ajout d’un PDF ;
- l’ajout d’une image ;
- la configuration des accès rapides ;
- la gestion des thèmes ;
- la gestion des profils ;
- le fonctionnement du mode TV ;
- la configuration Home Assistant ;
- la définition d’une action dans un bloc Markdown ;
- la configuration des contextes, de leur racine et des disponibilités ;
- la protection des pages et actions, l’authentification transmise par l’appelant et le PIN ;
- l’utilisation des directives Markdown ;
- la configuration MQTT ;
- MQTT Discovery ;
- la sauvegarde ;
- la restauration ;
- la résolution des problèmes courants ;
- la stratégie de mise à jour ;
- les idées d’évolution.

Ajouter également une documentation spécifique sur :

- l’architecture ;
- les directives Markdown ;
- Home Assistant ;
- MQTT ;
- la sécurité ;
- la sauvegarde et la restauration ;
- les conventions de contribution.

L’organisation et les noms exacts des fichiers de documentation sont laissés à ton appréciation.

La documentation principale doit néanmoins être immédiatement identifiable à la racine du dépôt.

# 46. Qualité du code

Configurer :

- formatage C# ;
- analyseurs .NET appropriés ;
- ESLint ;
- Prettier ;
- vérification TypeScript ;
- scripts cohérents ;
- tests unitaires ;
- tests d’intégration ;
- tests du front ;
- vérification de build.

Ajouter les commandes nécessaires pour :

- lancer le backend ;
- lancer le frontend ;
- lancer l’ensemble en développement ;
- compiler ;
- vérifier le formatage ;
- lancer le lint ;
- lancer le typecheck ;
- lancer les tests ;
- lancer Docker Compose.

Documenter toutes les commandes.

# 47. Tests attendus

Ajouter des tests vérifiant au minimum :

- validation des contenus Markdown ;
- parsing des directives ;
- parsing, validation et compatibilité des blocs d’action délimités ;
- rejet d’une directive inconnue ;
- réponse 404 pour un contexte ou un profil absent ou inconnu ;
- disponibilité universelle d’une page ou d’une action dont la liste de contextes est vide ;
- redirection d’une page indisponible vers la racine du contexte ;
- accès direct à une page disponible située hors de la racine du contexte ;
- création de pages, sous-pages et liens vers d’autres pages sans duplication du contenu ;
- réordonnancement de l’arborescence par glisser-déposer ;
- respect de la disponibilité et de la protection de la page cible lorsqu’elle est atteinte par un lien ;
- détection des liens invalides et des boucles de résolution de liens ;
- maintien du contexte et du profil dans la navigation ;
- action indisponible visible, grisée et désactivée, et refus côté serveur ;
- exécution d’un bloc valide du catalogue avec sa cible et sa valeur fixées dans le contenu publié ;
- rendu du texte, du logo et de la classe CSS du bloc sans modification des règles d’autorisation ni transmission de ces données à l’intégration ;
- rejet des types et opérations non pris en charge et des paramètres arbitraires envoyés par le navigateur ;
- accès aux pages et actions protégées par authentification valide de l’appelant ou PIN commun ;
- validation d’un token signé d’un appelant autorisé et rejet des tokens invalides ou expirés ;
- autorisation temporaire partagée dans le contexte, sans accès aux éléments indisponibles ;
- expiration après 30 secondes d’inactivité et invalidation au changement de contexte ;
- écran de saisie du PIN et retour à la racine après expiration ;
- création ou modification des contenus réservée au back-office administrateur ;
- traduction de l’interface d’administration sans création de variantes linguistiques des contenus ;
- choix initial de la langue du navigateur et prise en compte de la préférence de langue enregistrée ;
- création du premier compte par l’onboarding et refus de réutiliser l’onboarding non authentifié après initialisation ;
- gestion du compte, du mot de passe, du PIN et de la configuration Home Assistant dans les pages de paramètres correspondantes ;
- surcharge des réglages de design et ajout de CSS par device sans modification des règles de navigation ou d’autorisation ;
- filtrage des résultats de recherche par contexte ;
- affichage du titre, du résumé et d’une icône de verrouillage pour une page protégée disponible dans le contexte, sans extrait du corps avant autorisation ;
- insertion et utilisation des médias depuis l’éditeur Markdown ;
- fonctionnement sur le réseau local sans accès Internet ;
- chiffrement des secrets ;
- absence des secrets dans les réponses API ;
- appel simulé Home Assistant ;
- règles métier testées avec des opérations de persistance simulées, sans dépendance au moteur ;
- opérations SQLite et migrations vérifiées par des tests d’intégration ;
- utilisation de l’URL et du jeton longue durée configurés pour un appel simulé à l’API Home Assistant ;
- gestion de l’indisponibilité Home Assistant ;
- publication de disponibilité ;
- reconnexion MQTT ;
- génération MQTT Discovery et stabilité des unique_id MQTT lors de l’étape correspondante ;
- navigation clavier ;
- navigation à la télécommande ;
- édition d’une page ;
- prévisualisation d’une page ;
- publication d’une page ;
- validation CSS ;
- sauvegarde ;
- vérification d’une archive de sauvegarde ;
- restauration.

Utiliser des connecteurs simulés dans les tests.

Aucun test automatisé ne doit exécuter une action réelle sur Home Assistant ou sur un broker MQTT réel.

# 48. Contenus de démonstration

Créer suffisamment de contenu de démonstration pour présenter réellement l’application.

Les contenus doivent être crédibles, mais clairement fictifs.

Créer au minimum les données suivantes.

## Pièces

- Cuisine
- Salon
- Buanderie
- Salle de bain
- Chambre
- Garage
- Extérieur

## Emplacements

- Ampoules
- Piles
- Sacs poubelles
- Essuie-tout
- Produits ménagers
- Aspirateur
- Rallonge électrique
- Serviettes pour les invités

## Procédures

- Instructions générales de ménage
- Ménage de la cuisine
- Sortir les poubelles
- Préparer la maison avant un départ prolongé
- Accueillir des invités
- Redémarrer la télévision de la cuisine

## Équipements

- Four de la cuisine
- Plaque de cuisson
- Lave-vaisselle
- Hotte
- Réfrigérateur

## Dépannage

- Plus d’électricité
- Fuite d’eau
- Chauffage
- Plus d’Internet
- Domotique en panne
- Porte ou portail
- Appareil de cuisine
- Qui appeler ?

## Documentation technique

- Présentation de Home Assistant
- Présentation du réseau
- Présentation de MQTT
- Procédure de sauvegarde fictive
- Procédure de restauration fictive

Ne pas inventer d’adresses IP, d’identifiants, de mots de passe, de numéros de série ou de caractéristiques réelles présentées comme authentiques.

# 49. Hors périmètre de cette version

Ne pas implémenter pour l’instant :

- connecteur Gladys ;
- connecteur Manoir ;
- catalogue d’actions personnalisable et registre d’actions approuvées ;
- actions MQTT déclenchées depuis les pages ;
- plan de maison interactif complexe ;
- éditeur visuel de pages de type Notion ;
- base de données de stock avec quantités ;
- notifications d’entretien complexes ;
- scripts arbitraires dans le Markdown ;
- exécution de JavaScript depuis le Markdown ;
- automatisations conditionnelles complexes ;
- magasin de widgets ;
- synchronisation cloud ;
- dépendance à un service SaaS ;
- OpenID Connect ;
- accès direct du navigateur à Home Assistant ;
- accès direct du navigateur au broker MQTT.

Prévoir les interfaces et points d’extension nécessaires pour Gladys et Manoir, sans créer de fausses implémentations fonctionnelles.

Ne pas surconcevoir ces futures intégrations.

# 50. Méthode de travail

Développer progressivement, par étapes fonctionnelles. La liste suivante décrit les travaux du périmètre global et leurs dépendances possibles, pas l’obligation de tout livrer dans la première étape :

1. Inspecter le dépôt.
2. Identifier les éléments existants réutilisables.
3. Définir l’architecture générale adaptée au dépôt.
4. Mettre en place le domaine métier.
5. Mettre en place le stockage SQLite.
6. Mettre en place la gestion des fichiers Markdown.
7. Créer les schémas de contenus.
8. Ajouter les contenus de démonstration.
9. Créer l’API.
10. Créer l’authentification et les autorisations.
11. Créer le front public.
12. Créer le back-office.
13. Implémenter la recherche.
14. Implémenter les thèmes.
15. Implémenter les profils d’affichage et les contextes d’utilisation.
16. Implémenter les écrans enregistrés et leurs overrides de design.
17. Implémenter la navigation tactile, clavier et télécommande.
18. Créer le mini-catalogue intégré d’actions domotiques.
19. Créer le parseur Markdown enrichi.
20. Implémenter le connecteur Home Assistant.
21. Implémenter les états en temps réel.
22. Implémenter MQTT.
23. Ajouter progressivement MQTT Discovery et les entités associées.
24. Vérifier les règles de contexte et de protection des pages et actions.
25. Ajouter les sauvegardes.
26. Ajouter les journaux.
27. Étudier ultérieurement un éventuel support PWA et une consultation sans serveur.
28. Ajouter le déploiement Docker.
29. Ajouter les tests.
30. Exécuter les vérifications.
31. Corriger toutes les erreurs.
32. Documenter le projet.

Cet ordre décrit les dépendances fonctionnelles attendues. Il ne constitue pas une recommandation d’organisation des dossiers ou des projets.

Ne demande pas de validation à chaque étape.

Si un détail est ambigu, privilégier :

- la sécurité ;
- la simplicité ;
- la maintenance ;
- le fonctionnement local ;
- la lisibilité du Markdown ;
- l’accessibilité ;
- les performances ;
- l’usage tactile ;
- l’usage à la télécommande ;
- l’extensibilité raisonnable ;
- l’absence de dépendance au cloud.

# 51. Liberté d’organisation technique

Dans le respect du découpage Contracts / métier et de l’abstraction de persistance définis en section 3.1, tu es libre de choisir :

- le nombre de projets ;
- le découpage des modules ;
- les noms des projets ;
- les noms des dossiers ;
- l’organisation des composants React ;
- l’organisation des contrôleurs ou endpoints ;
- l’emplacement des contenus ;
- l’emplacement des médias ;
- l’emplacement des tests ;
- l’organisation des migrations ;
- l’organisation des fichiers CSS ;
- l’organisation de la documentation ;
- le découpage des fichiers Docker.

Ne crée pas une architecture artificiellement complexe uniquement pour respecter un modèle théorique.

Ne crée pas un projet distinct pour chaque responsabilité si une séparation modulaire dans un même projet est plus simple et suffisamment claire.

Inversement, ne concentre pas toute l’application dans quelques fichiers volumineux.

Justifie brièvement les choix d’organisation retenus dans la documentation d’architecture.

# 52. Critères de fin

Chaque étape doit être fonctionnelle, vérifiée et documentée pour le périmètre qu’elle couvre. Le périmètre global n’est terminé que si :

- la solution compile ;
- le front compile ;
- les migrations SQLite fonctionnent ;
- les tests passent ;
- le lint passe ;
- le typecheck passe ;
- l’application démarre ;
- Docker Compose démarre l’application ;
- le healthcheck fonctionne ;
- le compte administrateur initial peut être créé ;
- une page peut être créée et publiée ;
- une directive Markdown peut afficher un état simulé ;
- une action définie dans un bloc valide peut être exécutée avec un connecteur simulé ;
- une action indisponible dans le contexte ou protégée sans autorisation est refusée ;
- les règles de contexte, de PIN et d’expiration sont vérifiées ;
- la présence MQTT est publiée et, lorsque l’étape Discovery est réalisée, ses configurations peuvent être générées ;
- les contenus et paramètres peuvent être sauvegardés ;
- la documentation de démarrage est complète.

# 53. Résultat attendu

À la fin, fournir un résumé précis comprenant :

- l’architecture retenue ;
- les choix d’organisation effectués et leur justification ;
- les principaux éléments créés ;
- les technologies utilisées ;
- le modèle de données ;
- le fonctionnement du stockage Markdown ;
- le fonctionnement du back-office ;
- le fonctionnement des profils d’affichage ;
- le fonctionnement des écrans enregistrés ;
- le fonctionnement des contextes et de l’arborescence ;
- le fonctionnement du Markdown enrichi ;
- le fonctionnement du catalogue intégré d’actions ;
- l’intégration Home Assistant ;
- l’intégration MQTT ;
- MQTT Discovery ;
- la sécurité mise en place ;
- la procédure de création du premier administrateur ;
- la procédure de démarrage en développement ;
- la procédure de démarrage avec Docker ;
- la procédure de sauvegarde ;
- les commandes exécutées ;
- les résultats des tests ;
- les limites restant à traiter.

Après validation du brief et demande explicite d’implémentation, inspecte le dépôt, établis rapidement son état initial, puis implémente réellement la solution.

Ne réponds pas uniquement avec une proposition d’architecture.

Ne crée pas seulement une documentation ou un squelette vide.

Crée une première version fonctionnelle, exécutable, testée et démontrable.