namespace Unload.Api;

/// <summary>
/// Вычисляет рабочие директории runtime для API.
/// </summary>
internal static class ApiWorkspacePathResolver
{
    public static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var catalog = Path.Combine(current.FullName, "configs", "catalog.json");
            var scripts = Path.Combine(current.FullName, "scripts");

            if (File.Exists(catalog) && Directory.Exists(scripts))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Workspace root not found. Expected folders: 'configs' with 'catalog.json' and 'scripts'.");
    }

}
