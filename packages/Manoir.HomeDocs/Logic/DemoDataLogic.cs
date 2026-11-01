using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;

namespace Manoir.HomeDocs.Logic;

public enum DemoDataInstallResult
{
    Installed,
    AlreadyInstalled,
    Conflict
}

public sealed class DemoDataLogic(
    ContentPageLogic pages,
    PageTreeLogic tree,
    UsageContextLogic contexts)
{
    private const string DemoNotice = "**Contenu fictif de démonstration.**";
    private static readonly IReadOnlyList<DemoPage> SeedPages = CreateSeedPages();
    private static readonly IReadOnlyDictionary<string, string> ContextNames = new Dictionary<string, string>
    {
        ["invites"] = "Invités (démo)",
        ["menage"] = "Ménage (démo)",
        ["habitant"] = "Habitants (démo)"
    };
    private static readonly IReadOnlyDictionary<string, string[]> QuickLinks = new Dictionary<string, string[]>
    {
        ["invites"] = ["wifi-invite", "accueillir-invites", "serviettes-invites"],
        ["menage"] = ["instructions-menage", "menage-cuisine", "sortir-poubelles", "produits-menagers", "aspirateur"]
    };

    public async Task<DemoDataInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        var existingContexts = (await contexts.ListAsync(cancellationToken)).ToDictionary(item => item.Id);
        var existingPages = await pages.ListAsync(cancellationToken);
        var pagesBySlug = existingPages.ToDictionary(page => page.Slug, StringComparer.Ordinal);
        var existingNodes = await tree.ListAsync(cancellationToken);
        foreach (var (id, name) in ContextNames)
        {
            if (!existingContexts.TryGetValue(id, out var existing)) continue;
            if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                return DemoDataInstallResult.Conflict;
            if (existing.RootNodeId is not Guid rootNodeId) continue;
            var rootNode = existingNodes.SingleOrDefault(node => node.Id == rootNodeId);
            var rootSlug = rootNode is null
                ? null
                : existingPages.SingleOrDefault(page => page.Id == rootNode.PageId)?.Slug;
            if (!string.Equals(rootSlug, RootSlug(id), StringComparison.Ordinal))
                return DemoDataInstallResult.Conflict;
        }

        foreach (var seed in SeedPages)
        {
            if (pagesBySlug.TryGetValue(seed.Slug, out var existing) && !Matches(existing, seed))
                return DemoDataInstallResult.Conflict;
        }

        var changed = false;
        foreach (var (id, name) in ContextNames)
        {
            if (existingContexts.ContainsKey(id)) continue;
            EnsureContextResult(await contexts.SaveAsync(null, id, name, null, cancellationToken));
            changed = true;
        }

        var pageIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var seed in SeedPages.Where(page => page.Contexts.Count > 0))
        {
            var (pageId, wasChanged) = await EnsurePageAsync(seed, null, pagesBySlug, cancellationToken);
            pageIds.Add(seed.Slug, pageId);
            changed |= wasChanged;
        }

        var rootNodeIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var contextId in ContextNames.Keys)
        {
            var rootPageId = pageIds[RootSlug(contextId)];
            var (rootNode, nodeChanged) = await EnsurePageNodeAsync(rootPageId, null, cancellationToken);
            changed |= nodeChanged;
            rootNodeIds.Add(contextId, rootNode.Id);
            var current = await contexts.FindAsync(contextId, cancellationToken)
                ?? throw new InvalidOperationException($"Demo context '{contextId}' disappeared during installation.");
            if (current.RootNodeId == rootNode.Id) continue;
            EnsureContextResult(await contexts.SaveAsync(
                contextId,
                contextId,
                ContextNames[contextId],
                rootNode.Id,
                cancellationToken));
            changed = true;
        }

        var habitantRoot = rootNodeIds["habitant"];
        foreach (var seed in SeedPages.Where(page => page.Contexts.Count == 0))
        {
            var (pageId, wasChanged) = await EnsurePageAsync(seed, habitantRoot, pagesBySlug, cancellationToken);
            pageIds.Add(seed.Slug, pageId);
            changed |= wasChanged;
        }

        foreach (var (contextId, slugs) in QuickLinks)
        {
            var rootNodeId = rootNodeIds[contextId];
            var nodes = await tree.ListAsync(cancellationToken);
            foreach (var slug in slugs)
            {
                var targetPageId = pageIds[slug];
                if (nodes.Any(node =>
                        node.ParentNodeId == rootNodeId &&
                        node.PageId == targetPageId &&
                        node.IsLink))
                    continue;
                var result = await tree.CreateLinkAsync(targetPageId, rootNodeId, cancellationToken);
                if (result != PageTreeResult.Success)
                    throw new InvalidOperationException($"Could not create demo navigation link for '{slug}': {result}.");
                changed = true;
            }
        }

        return changed ? DemoDataInstallResult.Installed : DemoDataInstallResult.AlreadyInstalled;
    }

    private async Task<(Guid PageId, bool Changed)> EnsurePageAsync(
        DemoPage seed,
        Guid? parentNodeId,
        IReadOnlyDictionary<string, ContentPage> existingPages,
        CancellationToken cancellationToken)
    {
        Guid id;
        var changed = false;
        if (existingPages.TryGetValue(seed.Slug, out var existing))
        {
            id = existing.Id;
            if (!existing.IsPublished)
            {
                var result = await pages.SetPublicationAsync(id, existing.Revision, true, cancellationToken);
                if (result != ContentPageResult.Success)
                    throw new InvalidOperationException($"Could not publish demo page '{seed.Slug}': {result}.");
                changed = true;
            }
        }
        else
        {
            var result = await pages.CreateAsync(seed.ToInput(), parentNodeId, cancellationToken);
            if (result.Result != ContentPageResult.Success)
                throw new InvalidOperationException($"Could not create demo page '{seed.Slug}': {result.Result}.");
            id = result.Id;
            var created = await pages.FindAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Created demo page '{seed.Slug}' could not be read.");
            var publishResult = await pages.SetPublicationAsync(id, created.Revision, true, cancellationToken);
            if (publishResult != ContentPageResult.Success)
                throw new InvalidOperationException($"Could not publish demo page '{seed.Slug}': {publishResult}.");
            changed = true;
        }

        var (_, nodeChanged) = await EnsurePageNodeAsync(id, parentNodeId, cancellationToken);
        changed |= nodeChanged;
        return (id, changed);
    }

    private async Task<(PageTreeNode Node, bool Changed)> EnsurePageNodeAsync(
        Guid pageId,
        Guid? parentNodeId,
        CancellationToken cancellationToken)
    {
        var matches = (await tree.ListAsync(cancellationToken))
            .Where(node => node.PageId == pageId && !node.IsLink)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Demo page '{pageId}' must have exactly one page node.");
        var node = matches[0];
        if (node.ParentNodeId == parentNodeId) return (node, false);
        var siblings = await tree.ListAsync(cancellationToken);
        var position = siblings.Count(item => item.ParentNodeId == parentNodeId);
        var result = await tree.MoveAsync(node.Id, parentNodeId, position, cancellationToken);
        if (result != PageTreeResult.Success)
            throw new InvalidOperationException($"Could not place demo page '{node.Slug}' in the navigation tree: {result}.");
        return (node with { ParentNodeId = parentNodeId }, true);
    }

    private static bool Matches(ContentPage page, DemoPage seed) =>
        string.Equals(page.Title, seed.Title, StringComparison.Ordinal) &&
        string.Equals(page.Category, seed.Category, StringComparison.Ordinal) &&
        page.Contexts.SequenceEqual(seed.Contexts, StringComparer.Ordinal) &&
        !page.RequiresAuthentication &&
        string.Equals(page.Markdown, seed.Markdown, StringComparison.Ordinal);

    private static void EnsureContextResult(ContextWriteResult result)
    {
        if (result != ContextWriteResult.Success)
            throw new InvalidOperationException($"Could not create or update a demo context: {result}.");
    }

    private static string RootSlug(string contextId) => $"accueil-{contextId}";

    private static IReadOnlyList<DemoPage> CreateSeedPages()
    {
        var pages = new List<DemoPage>
        {
            Seed("Manuel de la maison", "accueil-habitant", "page", "Ce manuel contient des exemples fictifs à remplacer par vos informations.", "habitant"),
            Seed("Bienvenue", "accueil-invites", "page", "Les informations de cette page sont fictives. Demandez aux habitants les consignes réelles de la maison.", "invites"),
            Seed("Accueil ménage", "accueil-menage", "page", "Ces procédures sont des exemples fictifs à adapter aux produits et habitudes du foyer.", "menage"),
            Seed("Cuisine", "cuisine", "piece", "La cuisine est présentée comme une pièce de démonstration. Les rangements et équipements listés ici sont fictifs."),
            Seed("Salon", "salon", "piece", "Le salon est une pièce d’exemple. Remplacez cette fiche par les informations réelles."),
            Seed("Buanderie", "buanderie", "piece", "Exemple fictif : la buanderie se trouve au rez-de-chaussée, près du garage."),
            Seed("Salle de bain", "salle-de-bain", "piece", "Cette fiche de salle de bain est fictive et doit être adaptée."),
            Seed("Chambre", "chambre", "piece", "Exemple fictif : les draps de rechange sont rangés dans le placard du couloir."),
            Seed("Garage", "garage", "piece", "Exemple fictif : le matériel de bricolage se trouve sur l’étagère nord."),
            Seed("Extérieur", "exterieur", "piece", "Exemple fictif : les outils de jardinage sont rangés dans l’abri."),
            Seed("Ampoules", "ampoules-usagees", "emplacement", "Les ampoules de rechange sont dans le garage, boîte « Électricité ». Les ampoules usagées vont au bac de recyclage du garage."),
            Seed("Piles", "piles", "emplacement", "Exemple fictif : les piles neuves sont dans le tiroir de l’entrée. Déposez les piles usagées dans la boîte de collecte du garage."),
            Seed("Sacs poubelles", "sacs-poubelles", "emplacement", "Exemple fictif : les sacs se trouvent dans le placard sous l’évier."),
            Seed("Essuie-tout", "essuie-tout", "emplacement", "Exemple fictif : le rouleau de réserve est rangé sur l’étagère haute de la buanderie."),
            Seed("Produits ménagers", "produits-menagers", "emplacement", "Les produits d’exemple sont rangés dans le placard de la buanderie. Vérifiez toujours les étiquettes et ne mélangez jamais les produits."),
            Seed("Aspirateur", "aspirateur", "emplacement", "Exemple fictif : l’aspirateur est rangé dans le placard de l’entrée. Son câble est accroché au support latéral."),
            Seed("Rallonge électrique", "rallonge-electrique", "emplacement", "Exemple fictif : la rallonge se trouve dans le garage, bac « Câbles ». Déroulez-la complètement avant usage."),
            Seed("Serviettes pour les invités", "serviettes-invites", "emplacement", "Exemple fictif : les serviettes propres sont dans le placard de la salle de bain, étagère du haut."),
            Seed("Instructions générales de ménage", "instructions-menage", "procedure", "Commencez par aérer les pièces, rassemblez le linge, puis nettoyez du haut vers le bas. Utilisez uniquement les produits adaptés et rangez le matériel après usage."),
            Seed("Ménage de la cuisine", "menage-cuisine", "procedure", "Nettoyez les surfaces avec un produit adapté, lavez l’évier, essuyez la table puis balayez le sol. Ne mélangez jamais les produits ménagers."),
            Seed("Sortir les poubelles", "sortir-poubelles", "procedure", "Triez les déchets selon les consignes locales. Fermez les sacs avant de les sortir et replacez un sac propre dans la poubelle."),
            Seed("Préparer la maison avant un départ prolongé", "depart-prolonge", "procedure", "Fermez les fenêtres, vérifiez les robinets, videz les déchets et débranchez les appareils non nécessaires. Cette liste est un exemple à adapter."),
            Seed("Accueillir des invités", "accueillir-invites", "procedure", "Préparez les serviettes, indiquez le Wi-Fi et expliquez où se trouvent les objets utiles. Les informations de cette fiche sont fictives."),
            Seed("Redémarrer la télévision de la cuisine", "redemarrer-tv-cuisine", "procedure", "Éteignez la télévision avec sa télécommande, attendez quelques secondes puis rallumez-la. Si le problème persiste, demandez l’aide d’un habitant."),
            Seed("Four de la cuisine", "four-cuisine", "equipement", "Exemple fictif : le four est dans la cuisine. Consultez la notice réelle avant de lancer un cycle de nettoyage."),
            Seed("Plaque de cuisson", "plaque-cuisson", "equipement", "Exemple fictif : vérifiez que les témoins sont éteints après usage. Ne posez pas d’objet sur une plaque encore chaude."),
            Seed("Lave-vaisselle", "lave-vaisselle", "equipement", "Exemple fictif : vérifiez le filtre régulièrement et utilisez un programme adapté à la charge."),
            Seed("Hotte", "hotte", "equipement", "Exemple fictif : nettoyez le filtre selon les recommandations du fabricant et coupez l’alimentation avant toute intervention."),
            Seed("Réfrigérateur", "refrigerateur", "equipement", "Exemple fictif : gardez les aérations dégagées et nettoyez les clayettes avec un produit adapté."),
            Seed("Plus d’électricité", "plus-electricite", "depannage", "Vérifiez si la panne concerne toute la maison ou une seule pièce. En cas de danger, ne touchez pas au tableau électrique et contactez un professionnel."),
            Seed("Fuite d’eau", "fuite-eau", "depannage", "Éloignez les appareils électriques de l’eau. Si cela ne présente pas de danger, fermez le robinet d’arrêt et prévenez un habitant."),
            Seed("Chauffage", "chauffage", "depannage", "Vérifiez le réglage de la commande et que les aérations ne sont pas obstruées. N’ouvrez pas l’appareil."),
            Seed("Plus d’Internet", "plus-internet", "depannage", "Vérifiez l’alimentation du routeur et attendez quelques minutes avant de le redémarrer. Les noms et paramètres réseau de cette démo sont fictifs."),
            Seed("Domotique en panne", "domotique-en-panne", "depannage", "Vérifiez d’abord l’alimentation et le réseau local. Ne réinitialisez pas les équipements sans l’accord d’un administrateur."),
            Seed("Porte ou portail", "porte-portail", "depannage", "Si la porte ou le portail ne répond pas, vérifiez qu’aucun obstacle ne gêne son mouvement. N’essayez pas de forcer le mécanisme."),
            Seed("Appareil de cuisine", "appareil-cuisine", "depannage", "Coupez l’appareil si une odeur inhabituelle, de la fumée ou un bruit anormal apparaît. Consultez sa notice et demandez conseil."),
            Seed("Qui appeler ?", "qui-appeler", "depannage", "Cette page ne contient volontairement aucun vrai numéro. Ajoutez ici les coordonnées vérifiées avant toute utilisation réelle."),
            Seed("Wi-Fi invité", "wifi-invite", "technique", "Le réseau et le mot de passe de cette démonstration sont fictifs. Ne les utilisez pas pour vous connecter à un réseau réel."),
            Seed("Couper l’eau", "couper-eau", "procedure", "Repérez le robinet d’arrêt principal indiqué dans la documentation réelle. En cas de doute ou de danger, contactez un professionnel."),
            Seed("Tableau électrique", "tableau-electrique", "technique", "Exemple fictif : le tableau est situé dans le garage. Ne manipulez pas les protections si vous ne savez pas quelle action effectuer."),
            Seed("Réseau et équipements de la maison", "reseau-maison", "technique", "Schéma de démonstration fictif : routeur, point d’accès et équipements connectés. Remplacez cette page par une documentation vérifiée."),
            Seed("Sauvegardes et récupération", "sauvegardes-recuperation", "technique", "Cette procédure est un exemple. Vérifiez régulièrement la présence d’une sauvegarde exploitable avant de supprimer des données.")
        };

        return pages;
    }

    private static DemoPage Seed(
        string title,
        string slug,
        string category,
        string body,
        string? contextId = null) =>
        new(title, slug, category, contextId is null ? [] : [contextId], body);

    private sealed record DemoPage(
        string Title,
        string Slug,
        string Category,
        IReadOnlyList<string> Contexts,
        string Body)
    {
        public string Markdown { get; } = $"{DemoNotice}\n\n{Body}";

        public ContentPageInput ToInput() =>
            new(Title, Slug, Category, Contexts, false, Markdown);
    }
}
