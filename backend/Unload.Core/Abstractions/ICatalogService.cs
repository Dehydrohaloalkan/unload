namespace Unload.Core;

public interface ICatalogService
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> profileCodes,
        CancellationToken cancellationToken);
}
