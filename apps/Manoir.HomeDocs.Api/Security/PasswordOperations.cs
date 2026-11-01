using Manoir.HomeDocs.Operations;
using Microsoft.AspNetCore.Identity;

namespace Manoir.HomeDocs.Api.Security;

public sealed class PasswordOperations(IPasswordHasher<object> hasher) : IPasswordOperations
{
    private static readonly object User = new();
    private readonly string dummyHash = hasher.HashPassword(User, Guid.NewGuid().ToString("N"));

    public string Hash(string password) => hasher.HashPassword(User, password);

    public bool Verify(string? passwordHash, string password)
    {
        var result = hasher.VerifyHashedPassword(User, passwordHash ?? dummyHash, password);
        return passwordHash is not null && result != PasswordVerificationResult.Failed;
    }
}
