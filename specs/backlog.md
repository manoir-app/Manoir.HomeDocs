# Backlog prévisionnel — Manuel de la maison

Ce fichier découpe le [brief](./brief.md) en travaux progressifs, destinés à devenir des issues GitHub et à être confiés individuellement à des agents de développement. Le brief reste la référence fonctionnelle ; le découpage et l’ordre proposés ici pourront évoluer.

Les identifiants `HD-xx` sont des repères locaux, pas des numéros d’issues. Chaque fiche est une proposition d’issue, avec son périmètre, ses prérequis et ses critères de fin. Aucune issue n’a encore été créée.

## Préparation des issues GitHub

Lors de la publication :

1. Rendre le brief et ce backlog accessibles dans le dépôt GitHub ; ils sont actuellement des fichiers locaux.
2. Créer une issue par fiche, en recopiant aussi les **consignes communes** ci-dessous et les sections pertinentes du brief ou leurs liens.
3. Remplacer les références `HD-xx` par les liens vers les issues créées ; conserver les identifiants dans les titres ou le corps pour faciliter la correspondance.
4. Fournir à l’agent le dépôt, la branche de départ et les prérequis effectivement intégrés. Une dépendance doit être disponible dans sa branche, pas simplement ouverte sur GitHub.
5. Si un choix reste ouvert, le signaler dans l’issue plutôt que de présenter une hypothèse comme une décision validée.

L’ordre des lots facilite la lecture, mais seuls les prérequis de chaque fiche déterminent si elle peut être commencée. Deux issues sans dépendance peuvent néanmoins modifier les mêmes fichiers : coordonner leur intégration et résoudre les conflits.

## Consignes communes à joindre à chaque issue

- Lire le [brief](./brief.md), les sections mentionnées et le code existant avant de modifier le projet. Ne réaliser que le périmètre de l’issue ; respecter les exclusions indiquées.
- Suivre le découpage MaNoir **Contracts / métier**, avec **Logic / Operations** dans le métier. Les Logic dépendent d’interfaces d’opérations, pas directement de SQLite. Implémenter SQLite avec EF Core ; ne pas implémenter MongoDB.
- Les fichiers Markdown et médias restent persistants localement. Toute modification du contenu par l’application passe par le back-office administrateur ; ne pas prévoir d’édition directe des fichiers hors application.
- Le front fonctionne sur le réseau local sans accès à Internet. L’interface d’administration est en français et anglais ; les contenus éditoriaux ne sont pas multilingues.
- Contrôler les autorisations côté serveur. Les paramètres du navigateur, les profils, les identifiants de device ou la présentation ne constituent pas une preuve d’autorisation.
- Ne jamais exposer ou journaliser de mot de passe, PIN, token ou autre secret. Tester les connecteurs avec des simulations : aucune action réelle sur Home Assistant ni sur un broker MQTT réel.
- Ajouter les tests et la documentation directement liés au travail ; exécuter les vérifications pertinentes disponibles et signaler tout blocage. Ne pas attendre une issue finale pour sécuriser ou tester le code livré.
- Signaler les hypothèses, limites et éventuels écarts avec le brief dans le résultat de l’issue. Ne pas présenter une fonctionnalité simulée comme une intégration réelle.

---

## Lot 1 — Socle, sécurité et manuel consultable

### HD-01 — Mettre en place le socle applicatif et le déploiement local

**Prérequis :** aucun.  
**Brief :** sections 2, 3, 37, 39, 43, 45 et 46.

**Périmètre**

- Examiner le dépôt et créer le socle ASP.NET Core 10, React/TypeScript strict et Vite, avec séparation claire des responsabilités.
- Prévoir la configuration locale, les volumes persistants, Docker Compose, le healthcheck et l’exposition du port configurable.
- Mettre en place les commandes de développement, vérification, build et tests, et une documentation de démarrage minimale.

**Hors périmètre :** fonctionnalités métier, connecteurs et fausses données persistées.

**Critères de fin**

- [x] L’application démarre en développement et avec `docker compose up -d --build` ; son healthcheck répond.
- [x] Le frontend et le backend démarrent sans service Internet et sans secret réel dans le dépôt.
- [x] Les commandes et les résultats des vérifications disponibles sont documentés.

### HD-27 — Structurer le métier et la persistance SQLite

**Prérequis :** HD-01.  
**Brief :** sections 3.1, 4 et 47.

**Périmètre**

- Mettre en place les contrats publics, le projet métier et son découpage Logic / Operations.
- Définir les interfaces nécessaires aux premières données de configuration ; injecter les opérations dans la logique.
- Ajouter EF Core, SQLite et les migrations dans l’implémentation de persistance ; prévoir des opérations simulées dans les tests.

**Hors périmètre :** implémentation MongoDB, framework générique de persistance et remplacement des fichiers Markdown ou médias.

**Critères de fin**

- [x] Les contrats et Logic n’exposent ni `DbContext`, ni types EF Core, ni types propres à un moteur.
- [x] Une opération de persistance réelle et ses migrations SQLite sont testées en intégration ; la logique correspondante est testée avec des opérations simulées.
- [x] Les futures opérations peuvent être ajoutées sans réécrire les règles métier pour changer de moteur.

### HD-02 — Créer le premier administrateur et sécuriser le back-office

**Prérequis :** HD-01, HD-27.  
**Brief :** sections 20, 36, 44 et 47.

**Périmètre**

- Créer la page d’onboarding du premier administrateur, la connexion locale et la protection des routes et API administratives.
- Stocker et vérifier le mot de passe de façon sûre, sans identifiant ou mot de passe par défaut permanent.
- Empêcher la réutilisation non authentifiée de l’onboarding après initialisation, y compris lors de demandes concurrentes.

**Hors périmètre :** détection d’autres applications MaNoir et réutilisation de leur compte.

**Critères de fin**

- [x] Un premier compte est créé depuis l’onboarding puis permet de se connecter.
- [x] Les accès administratifs non authentifiés et toute nouvelle initialisation non autorisée sont refusés côté serveur.
- [x] Les tests couvrent l’initialisation, la connexion et les refus ; la procédure est documentée.

### HD-03 — Stocker et publier des pages Markdown

**Prérequis :** HD-02, HD-27.  
**Brief :** sections 4, 22, 24 et 38.

**Périmètre**

- Enregistrer des pages Markdown avec métadonnées validées, identifiants et slugs stables, brouillons et publication.
- Fournir les opérations et endpoints administratifs de création, lecture, modification, duplication, suppression et publication.
- Détecter les doublons et conserver une redirection lorsqu’un slug publié change.

**Hors périmètre :** éditeur enrichi, arborescence et affichage public ; ils relèvent des issues suivantes.

**Critères de fin**

- [ ] Un administrateur peut gérer et publier une page via les opérations et API ; les accès non administratifs en écriture sont refusés.
- [ ] Le Markdown stocké est lisible ; les doublons et les redirections de slug sont testés.
- [ ] Aucun endpoint public ne divulgue une page protégée avant la mise en place de ses contrôles d’accès.

### HD-04 — Administrer l’arborescence et les liens entre pages

**Prérequis :** HD-03.  
**Brief :** sections 6.1 et 22.

**Périmètre**

- Créer et organiser des pages et sous-pages dans une arborescence commune.
- Permettre qu’une sous-page soit un lien vers une autre page ou sous-page sans dupliquer son contenu.
- Réordonner par glisser-déposer avec alternative clavier ; signaler les liens invalides et les boucles de résolution.

**Hors périmètre :** racines et filtres de contexte, gérés par HD-05.

**Critères de fin**

- [ ] L’administrateur peut créer, lier et réordonner les éléments avec souris et clavier.
- [ ] Un lien retrouve la page cible sans créer de seconde copie ; les liens invalides et boucles sont testés.

### HD-05 — Gérer les contextes et les racines de navigation

**Prérequis :** HD-03, HD-04.  
**Brief :** sections 6.1 et 38.

**Périmètre**

- Gérer les contextes, leur identifiant, leur nom et leur racine depuis l’administration.
- Appliquer la liste de contextes d’une page : liste vide disponible partout ; page indisponible redirigée vers la racine du contexte.
- Permettre l’accès direct à une page disponible hors racine et appliquer à un lien les règles de sa cible.
- Retourner 404 lorsque le contexte est absent ou inconnu.

**Hors périmètre :** choix automatique du contexte, profils de device et authentification par PIN.

**Critères de fin**

- [ ] Les contextes invités, ménage et habitant peuvent partager l’arborescence avec des racines distinctes.
- [ ] Les cas liste vide, accès direct hors racine, lien et redirection sont testés.
- [ ] Une page protégée n’est jamais exposée en attendant HD-15 : la disponibilité par contexte ne suffit pas à autoriser sa lecture.

### HD-06 — Identifier le profil de device dans l’invocation

**Prérequis :** HD-05.  
**Brief :** sections 15, 16 et 38.

**Périmètre**

- Prendre en charge un profil explicite et un identifiant de device facultatif, distincts du contexte.
- Fournir les profils de base desktop, mobile, tablet-wall, tablet-dock et tv.
- Conserver profil et contexte dans la navigation ; retourner 404 si le profil est absent ou inconnu.

**Hors périmètre :** édition avancée des thèmes et overrides, traitée dans HD-30 et HD-13.

**Critères de fin**

- [ ] Une invocation valide utilise le contexte et le profil demandés ; le device facultatif ne modifie pas les droits.
- [ ] Les profils absents ou inconnus sont refusés ; liens et redirections conservent les paramètres nécessaires.

### HD-26 — Organiser les pages de paramètres de l’administration

**Prérequis :** HD-02.  
**Brief :** sections 20.1, 26 et 36.

**Périmètre**

- Créer les pages de paramètres Préférences UI, Sécurité et Intégration Home Assistant.
- Permettre la gestion du compte administrateur, de son mot de passe et du PIN commun dans Sécurité.
- Prévoir l’ajout de pages d’autres intégrations ; les paramètres effectifs de Home Assistant seront branchés par HD-16.

**Hors périmètre :** connecteur Manoir et authentification transmise par JWT.

**Critères de fin**

- [ ] Les pages et opérations sont réservées à l’admin ; la modification du compte, du mot de passe et du PIN est testée.
- [ ] Les secrets ne sont pas affichés en clair, renvoyés après enregistrement ni journalisés.

### HD-25 — Traduire l’interface d’administration en français et anglais

**Prérequis :** HD-02, HD-26.  
**Brief :** sections 20 et 20.1.

**Périmètre**

- Traduire les textes de l’administration, notamment menus, formulaires, validations et messages.
- Utiliser par défaut la langue prise en charge du navigateur, avec un repli explicite.
- Permettre de choisir et conserver la langue dans Préférences UI ; ce choix prime sur le navigateur.

**Hors périmètre :** traduction ou variantes linguistiques des contenus du manuel.

**Critères de fin**

- [ ] Les interfaces administratives existantes sont disponibles en français et anglais.
- [ ] La langue du navigateur, le repli et la préférence enregistrée sont testés.
- [ ] Aucune page ou ressource éditoriale n’est dupliquée pour gérer la langue.

### HD-15 — Protéger les pages et actions par PIN

**Prérequis :** HD-05, HD-26.  
**Brief :** sections 28 et 36.

**Périmètre**

- Protéger les pages et préparer les règles communes aux futures actions par le PIN configuré dans Sécurité.
- Accorder une autorisation temporaire pour les éléments protégés disponibles dans le contexte actif.
- Invalider cette autorisation au changement de contexte ou après 30 secondes d’inactivité ; afficher la saisie du PIN et un retour à la racine.

**Hors périmètre :** authentification par token d’un appelant, traitée séparément dans HD-28.

**Critères de fin**

- [ ] Une page protégée est refusée côté serveur sans autorisation et accessible avec le PIN correct dans le contexte permis.
- [ ] Le délai d’inactivité, le changement de contexte, le retour à la racine et le refus d’un contexte indisponible sont testés.
- [ ] Le PIN ne figure pas en clair dans le stockage, les réponses ou les journaux.

### HD-07 — Créer le front de consultation et les contenus de démonstration

**Prérequis :** HD-03, HD-05, HD-06, HD-15.  
**Brief :** sections 5, 6, 8 à 13 et 48.

**Périmètre**

- Afficher l’accueil du contexte, l’arborescence et les pages avec une interface française, tactile et responsive.
- Fournir les accès rapides configurables par contexte et les parcours de démonstration prévus au brief.
- Créer des contenus fictifs clairement identifiés pour les pièces, objets, procédures, équipements, dépannage et documentation technique.

**Hors périmètre :** recherche avancée, navigation TV complète et actions domotiques.

**Critères de fin**

- [ ] Les parcours de démonstration sont consultables depuis les contextes prévus, sans données prétendument réelles.
- [ ] Les pages indisponibles ou protégées respectent les règles serveur de HD-05 et HD-15.
- [ ] Les liens directs et QR codes conservent un contexte et un profil valides.

---

## Lot 2 — Édition, recherche et présentation

### HD-08 — Intégrer un éditeur Markdown avec aperçu

**Prérequis :** HD-03, HD-25.  
**Brief :** sections 22 et 23.

**Périmètre**

- Ajouter au back-office l’édition Markdown, la coloration syntaxique, les métadonnées, l’aperçu et les erreurs avant publication.
- Relier l’interface aux opérations d’administration de HD-03.

**Hors périmètre :** insertion intelligente des médias et des blocs domotiques, traitées dans HD-09 et HD-29.

**Critères de fin**

- [ ] L’admin peut créer, éditer, prévisualiser et publier une page ; les erreurs de validation sont visibles.
- [ ] Les tests couvrent le parcours d’édition et les accès non authentifiés sont refusés.

### HD-09 — Gérer les médias et leur insertion intelligente

**Prérequis :** HD-08.  
**Brief :** sections 23 et 40.

**Périmètre**

- Importer, rechercher, classer et réutiliser images et PDF dans une bibliothèque locale persistante.
- Insérer un média dans le Markdown sans saisir son chemin ; gérer son titre et le texte alternatif des images.
- Identifier les pages qui utilisent un média et avertir ou empêcher la suppression d’un média utilisé.

**Critères de fin**

- [ ] Un média importé peut être inséré dans une page et réutilisé depuis l’éditeur.
- [ ] Les utilisations et le comportement de suppression sont testés ; les fichiers survivent au redémarrage.

### HD-10 — Ajouter l’historique des contenus et le contrôle des liens

**Prérequis :** HD-03, HD-04, HD-08.  
**Brief :** sections 4.1 et 22.

**Périmètre**

- Conserver les versions et l’historique des publications ; permettre la restauration d’une version.
- Vérifier les liens et afficher les liens entrants dans le back-office.

**Critères de fin**

- [ ] Une version antérieure peut être restaurée sans casser les identifiants stables.
- [ ] Les liens entrants et références cassées sont identifiés dans des tests.

### HD-11 — Implémenter la recherche locale filtrée par contexte

**Prérequis :** HD-05, HD-07, HD-15.  
**Brief :** section 7.

**Périmètre**

- Rechercher dans les titres, résumés, contenus et métadonnées ; afficher des résultats au fil de la saisie et gérer les synonymes du brief.
- Ne retourner que les pages disponibles dans le contexte actif.
- Pour une page protégée disponible, afficher titre, résumé et icône de verrouillage ; ne pas exposer d’extrait du corps avant autorisation.

**Critères de fin**

- [ ] Les synonymes de démonstration retrouvent les contenus disponibles sans afficher les pages exclues du contexte.
- [ ] Les tests vérifient les résultats protégés avec et sans autorisation et l’absence de fuite du corps via l’API.

### HD-12 — Compléter la navigation tactile, clavier et télécommande

**Prérequis :** HD-06, HD-07.  
**Brief :** sections 14 et 19.

**Périmètre**

- Gérer les flèches directionnelles, Entrée et Retour/Échap, le focus visible et un retour prévisible.
- Appliquer les principes d’accessibilité et de lisibilité du brief aux parcours publics.

**Critères de fin**

- [ ] Les parcours principaux fonctionnent au clavier et avec une télécommande de type clavier, sans survol ni geste obligatoire.
- [ ] Le focus reste visible et les interactions tactiles conservent des zones suffisamment grandes.

### HD-30 — Gérer les thèmes et réglages de profils

**Prérequis :** HD-06, HD-25.  
**Brief :** sections 15, 17 et 18.1.

**Périmètre**

- Permettre à l’admin de gérer les thèmes et les réglages de présentation des profils, notamment typographie, couleurs, espacements, cartes et focus.
- Fournir un aperçu et appliquer les réglages au front avec prise en charge clair, sombre et préférences système.

**Hors périmètre :** override spécifique d’un device et éditeur CSS avancé.

**Critères de fin**

- [ ] Un changement de thème ou de profil est prévisualisable, sauvegardé et appliqué au profil demandé.
- [ ] Ces réglages ne modifient ni les contextes ni les permissions.

### HD-13 — Administrer les devices et leurs overrides de design

**Prérequis :** HD-06, HD-30.  
**Brief :** sections 16 et 18.

**Périmètre**

- Enregistrer les devices et gérer, depuis l’administration, leur fichier d’override de design.
- Permettre la surcharge de tous les réglages de design d’un profil et l’ajout de CSS supplémentaire par device.
- Appliquer l’override au seul device invoqué et proposer un aperçu.

**Hors périmètre :** modifier la racine, le contexte ou les permissions via l’override ; édition CSS avancée et historique.

**Critères de fin**

- [ ] Un device peut surcharger ses réglages et son CSS sans modifier le profil de référence ou un autre device.
- [ ] Les tests vérifient que l’override ne modifie pas la navigation ni les autorisations.

### HD-31 — Ajouter l’éditeur CSS avancé et son historique

**Prérequis :** HD-13, HD-30.  
**Brief :** section 18.2.

**Périmètre**

- Gérer le CSS commun, de thème, de profil et de device dans l’ordre prévu au brief.
- Ajouter coloration, validation, aperçu, historique, restauration et désactivation temporaire d’une surcharge incorrecte.

**Critères de fin**

- [ ] Une surcharge peut être prévisualisée, validée, publiée, désactivée et restaurée.
- [ ] L’ordre de cascade et les erreurs CSS sont testés.

### HD-14 — Afficher les écrans connectés et le tableau de bord

**Prérequis :** HD-02, HD-06, HD-13.  
**Brief :** sections 16 et 21.

**Périmètre**

- Suivre les devices connectés par SignalR et exposer leur état dans l’administration.
- Afficher la version, l’état de l’application et les informations disponibles sur les contenus ; ajouter les états des connecteurs et sauvegardes quand ces fonctionnalités existent.

**Critères de fin**

- [ ] Un device connecté apparaît puis disparaît ou passe hors ligne après déconnexion.
- [ ] Les fonctionnalités pas encore implémentées ne sont pas présentées comme opérationnelles.

---

## Lot 3 — Home Assistant et blocs domotiques

### HD-16 — Connecter Home Assistant et gérer ses secrets

**Prérequis :** HD-26.  
**Brief :** sections 25 et 26.

**Périmètre**

- Configurer côté serveur l’URL Home Assistant et le token longue durée ; chiffrer le token et ne jamais le renvoyer au navigateur.
- Tester la connexion, parcourir et rechercher les entités ; afficher clairement l’indisponibilité.
- Prévoir une abstraction de connecteur permettant plus tard d’ajouter Manoir, sans l’implémenter.

**Critères de fin**

- [ ] Une connexion simulée permet de lire des entités et de tester les erreurs de connexion.
- [ ] Aucun secret n’est exposé et le navigateur ne contacte pas directement Home Assistant.

### HD-17 — Parser et afficher les blocs d’action domotique

**Prérequis :** HD-08, HD-15, HD-16.  
**Brief :** sections 27 et 29.

**Périmètre**

- Définir et valider une syntaxe de bloc délimité extensible : intégration, type, action, cible, valeur fixe éventuelle et présentation locale.
- Prendre en compte les contextes autorisés et la protection ; prévoir compatibilité ou migration entre versions.
- Vérifier les capacités Home Assistant des types switch, scène, entité, automatisation et script ; signaler les cas non pris en charge.
- Rendre les blocs dans l’aperçu et le front, sans exécuter d’action.

**Hors périmètre :** endpoint d’exécution et appel de Home Assistant ; aucun bouton opérationnel avant HD-18.

**Critères de fin**

- [ ] Un bloc valide est parsé et rendu ; les blocs invalides ou non pris en charge produisent des erreurs explicites.
- [ ] Les actions sont visiblement indisponibles pour exécution tant que HD-18 n’est pas livré.
- [ ] Les tests couvrent les attributs, la présentation et la compatibilité de la syntaxe.

### HD-18 — Exécuter les actions avec contrôles serveur et journalisation

**Prérequis :** HD-15, HD-16, HD-17.  
**Brief :** sections 27 à 29 et 42.

**Périmètre**

- Ajouter en une même livraison l’exécution côté serveur, les contrôles d’autorisation et l’activation des boutons.
- Vérifier la page et le bloc publiés, leur disponibilité dans le contexte et l’éventuelle autorisation par PIN.
- Utiliser exclusivement l’intégration, la cible, l’opération et la valeur fixées dans le bloc publié ; refuser les paramètres arbitraires du client.
- Griser les actions indisponibles et journaliser exécutions et refus sans secrets.

**Hors périmètre :** restrictions par device, profil, horaire ou niveau de risque, et registre d’actions personnalisable.

**Critères de fin**

- [ ] Une action permise s’exécute avec un connecteur simulé ; une action interdite, protégée sans PIN ou issue d’un bloc non publié est refusée côté serveur.
- [ ] Les tests couvrent les appels directs, la falsification de cible ou valeur et les échecs du connecteur.
- [ ] Aucune action n’est rendue exécutable sans que tous ces contrôles soient actifs.

### HD-29 — Assister l’insertion des blocs domotiques dans l’éditeur

**Prérequis :** HD-08, HD-16, HD-17.  
**Brief :** sections 23 et 29.

**Périmètre**

- Aider l’admin à choisir un type d’action et une entité Home Assistant, renseigner ses paramètres et insérer un bloc Markdown.
- Afficher les erreurs de syntaxe, de contexte et de référence lors de la prévisualisation et avant publication.

**Critères de fin**

- [ ] Un admin peut insérer et prévisualiser un bloc valide sans écrire toute sa syntaxe.
- [ ] Les blocs invalides sont signalés et ne produisent pas d’exécution lors de l’édition ou de l’aperçu.

### HD-19 — Afficher les états domotiques en temps réel

**Prérequis :** HD-16, HD-17.  
**Brief :** sections 29 et 30.

**Périmètre**

- Rendre les états, cartes d’entités, groupes et affichages conditionnels décrits au brief.
- Mettre à jour les états, gérer cache court, reconnexion, état périmé et indisponibilité de Home Assistant.

**Critères de fin**

- [ ] Des états simulés s’affichent et se mettent à jour sans accès direct du navigateur à Home Assistant.
- [ ] Une déconnexion et un état périmé sont clairement signalés et testés.

### HD-28 — Accepter l’authentification signée d’un appelant

**Prérequis :** HD-15.  
**Brief :** section 28.

**Périmètre**

- Définir à l’implémentation le mécanisme de confiance et la transmission d’un token signé de type JWT par un appelant, sans secret dans l’URL.
- Vérifier signature, validité, émetteur autorisé et destinataire avant d’accorder l’autorisation temporaire.

**Priorité :** non bloquante pour la première protection par PIN. Ne pas prétendre que Home Assistant ou Manoir sait déjà émettre le token attendu.

**Critères de fin**

- [ ] Un token valide d’un appelant configuré accorde l’autorisation dans le contexte actif.
- [ ] Les tokens invalides, expirés, destinés à un autre service ou issus d’un émetteur non autorisé sont refusés.

---

## Lot 4 — Exploitation et présence MQTT

### HD-20 — Annoncer la présence de HomeDocs par MQTT

**Prérequis :** HD-02, HD-26.  
**Brief :** sections 31, 33 et 34.

**Périmètre**

- Configurer et tester le broker depuis l’administration, avec secrets protégés et reconnexion.
- Publier une présence stable et les états en ligne / hors ligne, y compris via Last Will en cas de coupure.

**Hors périmètre :** entités Discovery, commandes et administration avancée, traitées dans HD-23 et HD-24.

**Critères de fin**

- [ ] Les publications de présence, l’arrêt normal, le Last Will et la reconnexion sont vérifiés avec un broker simulé.
- [ ] Les paramètres et mots de passe ne sont ni exposés ni journalisés.

### HD-21 — Sauvegarder, vérifier et restaurer les données

**Prérequis :** HD-03, HD-09, HD-13, HD-31.  
**Brief :** sections 4 et 41.

**Périmètre**

- Sauvegarder SQLite, contenus, médias, thèmes, CSS et overrides, avec manifeste et versions.
- Gérer les secrets chiffrés et leur clé de restauration, la rétention et les fonctions d’administration prévues au brief.
- Documenter et tester la vérification d’archive et la restauration.

**Critères de fin**

- [ ] Une sauvegarde créée par l’admin peut être vérifiée et restaurée avec ses données persistantes.
- [ ] Les tests couvrent notamment une archive invalide et la gestion des clés et secrets, sans secrets réels.

### HD-22 — Compléter les journaux et la documentation d’exploitation

**Prérequis :** HD-16, HD-18, HD-20, HD-21.  
**Brief :** sections 42 et 45.

**Périmètre**

- Compléter les événements d’exploitation et d’administration déjà introduits dans les issues précédentes.
- Documenter installation, configuration, démarrage, sauvegarde, restauration, mise à jour et dépannage.

**Critères de fin**

- [ ] Les opérations principales sont traçables sans journaliser de secrets.
- [ ] La documentation permet à un administrateur d’exploiter et restaurer l’application.

---

## Lot 5 — Évolutions MQTT prévues

### HD-23 — Publier les entités MQTT Discovery

**Prérequis :** HD-14, HD-16, HD-20.  
**Brief :** sections 32 et 33.

**Périmètre**

- Ajouter la découverte Home Assistant, les identifiants stables, les états et les commandes prévus au brief.
- Gérer les publications retenues, la disponibilité et le retrait propre des configurations.

**Critères de fin**

- [ ] Les configurations et états attendus sont générés avec des identifiants stables, republiés et nettoyés.
- [ ] Les tests simulés couvrent disponibilité, retain et suppression des entités.

### HD-24 — Compléter l’administration MQTT

**Prérequis :** HD-20, HD-23.  
**Brief :** sections 33 et 34.

**Périmètre**

- Ajouter les aperçus de topics et payloads, la republication et le nettoyage de la découverte.
- Afficher les messages récents, erreurs de reconnexion et dates de connexion ; permettre les tests de publication par l’admin.

**Critères de fin**

- [ ] L’administrateur peut inspecter et gérer les publications Discovery sans accès aux mots de passe.
- [ ] Les opérations sont testées sans broker MQTT réel.

## À étudier ultérieurement, sans engagement d’implémentation

- Consultation sans serveur et éventuelle PWA, distinctes du fonctionnement local sans accès Internet.
- Connecteurs Manoir et Gladys ; persistance MongoDB commune ; catalogue d’actions personnalisable ; actions MQTT depuis les pages.
- Détection d’autres applications MaNoir à l’onboarding et réutilisation d’un compte existant après définition de la future intégration Manoir.
- Autres évolutions explicitement hors périmètre de la version actuelle dans le brief.
