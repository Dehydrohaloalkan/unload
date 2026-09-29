namespace Unload.Api.Models;

public sealed record DatabaseCredentialStatusResponse(
    bool Configured,
    string? SelectedDatabaseId,
    IReadOnlyList<DatabaseOptionResponse> Databases);

public sealed record DatabaseOptionResponse(string Id, string Name);

public sealed record SetDatabaseCredentialsRequest(
    string DatabaseId,
    string Username,
    string Password);
