using System.Text.Json;
using Manoir.HomeDocs.Contracts;
using Manoir.HomeDocs.Operations;
using Microsoft.EntityFrameworkCore;

namespace Manoir.HomeDocs.Persistence.Sqlite;

internal sealed class UsageContextOperations(HomeDocsDbContext context, ContentFileStore files) : IUsageContextOperations
{
    public async Task<IReadOnlyList<UsageContext>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.UsageContexts.AsNoTracking()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new UsageContext(item.Id, item.Name, item.RootNodeId))
            .ToListAsync(cancellationToken);

    public async Task<UsageContext?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        await context.UsageContexts.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new UsageContext(item.Id, item.Name, item.RootNodeId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ContextWriteResult> SaveAsync(
        string? currentId,
        UsageContext usageContext,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidId(usageContext.Id) || !IsValidName(usageContext.Name))
            return ContextWriteResult.InvalidInput;
        await using var gate = await files.LockAsync(cancellationToken);
        if (!await IsRootValidAsync(usageContext, cancellationToken))
            return ContextWriteResult.InvalidRoot;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        UsageContextEntity? current = null;
        if (currentId is not null)
        {
            current = await context.UsageContexts.SingleOrDefaultAsync(item => item.Id == currentId, cancellationToken);
            if (current is null) return ContextWriteResult.NotFound;
        }

        if (currentId != usageContext.Id &&
            await context.UsageContexts.AnyAsync(item => item.Id == usageContext.Id, cancellationToken))
            return ContextWriteResult.Conflict;

        if (current is not null && currentId != usageContext.Id && await IsUsedByPageAsync(currentId!, cancellationToken))
            return ContextWriteResult.InUse;

        if (current is null)
        {
            context.UsageContexts.Add(new UsageContextEntity
            {
                Id = usageContext.Id,
                Name = usageContext.Name.Trim(),
                RootNodeId = usageContext.RootNodeId
            });
        }
        else if (currentId == usageContext.Id)
        {
            current.Name = usageContext.Name.Trim();
            current.RootNodeId = usageContext.RootNodeId;
        }
        else
        {
            context.UsageContexts.Remove(current);
            context.UsageContexts.Add(new UsageContextEntity
            {
                Id = usageContext.Id,
                Name = usageContext.Name.Trim(),
                RootNodeId = usageContext.RootNodeId
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ContextWriteResult.Success;
    }

    public async Task<ContextWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var gate = await files.LockAsync(cancellationToken);
        var current = await context.UsageContexts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (current is null) return ContextWriteResult.NotFound;
        if (await IsUsedByPageAsync(id, cancellationToken)) return ContextWriteResult.InUse;
        context.UsageContexts.Remove(current);
        await context.SaveChangesAsync(cancellationToken);
        return ContextWriteResult.Success;
    }

    private async Task<bool> IsUsedByPageAsync(string id, CancellationToken cancellationToken)
    {
        var contexts = await context.ContentPages.AsNoTracking()
            .Select(page => page.ContextsJson)
            .ToListAsync(cancellationToken);
        return contexts.Any(json =>
            (JsonSerializer.Deserialize<string[]>(json)
                ?? throw new InvalidDataException("Invalid stored page contexts."))
            .Contains(id, StringComparer.Ordinal));
    }

    private async Task<bool> IsRootValidAsync(UsageContext usageContext, CancellationToken cancellationToken)
    {
        if (usageContext.RootNodeId is null) return true;
        var root = await context.PageTreeNodes.AsNoTracking()
            .Where(node => node.Id == usageContext.RootNodeId)
            .Select(node => new { node.Page.ContextsJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (root is null) return false;
        var contexts = JsonSerializer.Deserialize<string[]>(root.ContextsJson)
            ?? throw new InvalidDataException("Invalid stored page contexts.");
        return contexts.Length == 0 || contexts.Contains(usageContext.Id, StringComparer.Ordinal);
    }

    private static bool IsValidId(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') &&
        value[0] != '-' && value[^1] != '-' && !value.Contains("--", StringComparison.Ordinal);

    private static bool IsValidName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        value.Length <= 128 && !value.Any(char.IsControl);
}
