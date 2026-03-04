namespace Unload.Console;

/// <summary>
/// Вычисляет рабочие директории консольного приложения.
/// </summary>
internal static class WorkspacePathResolver
{
    public static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            var catalogPath = Path.Combine(current.FullName, "configs", "catalog.json");
            var scriptsPath = Path.Combine(current.FullName, "scripts");

            if (File.Exists(catalogPath) && Directory.Exists(scriptsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Workspace root not found. Expected folders: 'configs' with 'catalog.json' and 'scripts'.");
    }

}
