namespace Unload.Core;

public interface ICatalogService
{
    Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> profileCodes,
        CancellationToken cancellationToken);
}
