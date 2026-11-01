namespace Manoir.HomeDocs.Operations;

public interface IPasswordOperations
{
    string Hash(string password);

    bool Verify(string? passwordHash, string password);
}
