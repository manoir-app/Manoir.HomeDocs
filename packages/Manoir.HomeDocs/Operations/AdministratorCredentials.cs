namespace Manoir.HomeDocs.Operations;

public sealed record AdministratorCredentials(
    string UserName,
    string NormalizedUserName,
    string PasswordHash,
    string? PinHash = null,
    string Language = "fr");
