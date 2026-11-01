using Manoir.HomeDocs.Contracts;

namespace Manoir.HomeDocs.Operations;

public enum ContextWriteResult
{
    Success,
    InvalidInput,
    NotFound,
    Conflict,
    InUse,
    InvalidRoot
}

public interface IUsageContextOperations
{
    Task<IReadOnlyList<UsageContext>> ListAsync(CancellationToken cancellationToken = default);
    Task<UsageContext?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<ContextWriteResult> SaveAsync(string? currentId, UsageContext context, CancellationToken cancellationToken = default);
    Task<ContextWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
