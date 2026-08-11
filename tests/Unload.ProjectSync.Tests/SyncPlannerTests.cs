using Unload.ProjectSync;

namespace Unload.ProjectSync.Tests;

public sealed class SyncPlannerTests
{
    [Fact]
    public void CreatePlan_TransformsPathAndTextBeforeComparing()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WriteSource("backend/Unload.Api/Program.cs", "namespace Unload.Api;\n");
        workspace.WriteTarget("backend/IIU.Api/Program.cs", "namespace IIU.Api;\n");

        var plan = workspace.CreatePlan(CreateConfiguration());

        Assert.Empty(plan.Items);
        Assert.Equal(1, plan.SameCount);
    }

    [Fact]
    public void CreatePlan_DoesNotOfferProtectedOrTargetOnlyFilesForApply()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WriteSource("backend/Unload.Api/Program.cs", "namespace Unload.Api;\n");
        workspace.WriteSource("backend/Unload.Api/appsettings.Production.json", "{ \"source\": true }");
        workspace.WriteSource("backend/Unload.Api/bin/ignored.dll", "not a real dll");
        workspace.WriteTarget("backend/IIU.Api/Program.cs", "namespace IIU.Api; // old\n");
        workspace.WriteTarget("backend/IIU.Api/appsettings.Production.json", "{ \"target\": true }");
        workspace.WriteTarget("backend/IIU.Api/ProductionOnly.cs", "// keep me\n");

        var plan = workspace.CreatePlan(CreateConfiguration());

        Assert.Collection(
            plan.ApplicableItems,
            item =>
            {
                Assert.Equal(SyncAction.Update, item.Action);
                Assert.Equal("backend/IIU.Api/Program.cs", item.TargetRelativePath);
            });
        Assert.Contains(plan.Items, static item =>
            item.Action == SyncAction.Protected &&
            item.TargetRelativePath == "backend/IIU.Api/appsettings.Production.json");
        Assert.Contains(plan.Items, static item =>
            item.Action == SyncAction.TargetOnly &&
            item.TargetRelativePath == "backend/IIU.Api/ProductionOnly.cs");
        Assert.DoesNotContain(plan.Items, static item => item.TargetRelativePath.Contains("bin", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePlan_MapsFrontendDirectoryPrefix()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WriteSource("web/webApp/src/app/app.ts", "export const app = true;\n");
        workspace.WriteTarget("web/src/app/app.ts", "export const app = true;\n");

        var configuration = new SyncConfiguration
        {
            PathMappings = [new PathMappingRule("web/webApp", "web")]
        };

        var plan = workspace.CreatePlan(configuration);

        Assert.Empty(plan.Items);
        Assert.Equal(1, plan.SameCount);
    }

    [Fact]
    public void CreatePlan_MapsSymbolNamespaceAndAddsUsingToConsumers()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WriteSource(
            "backend/Unload.Core/Domain/RunnerEvent.cs",
            "namespace Unload.Core;\n\npublic record RunnerEvent;\n");
        workspace.WriteSource(
            "backend/Unload.Store/Consumer.cs",
            "using Unload.Core;\n\nnamespace Unload.Store;\n\npublic class Consumer(RunnerEvent value);\n");
        workspace.WriteTarget(
            "backend/IIU.Core/Domain/RunnerEvent.cs",
            "namespace IIU.Core.Domain;\n\npublic record RunnerEvent;\n");
        workspace.WriteTarget(
            "backend/IIU.Store/Consumer.cs",
            "using IIU.Core;\nusing IIU.Core.Domain;\n\nnamespace IIU.Store;\n\npublic class Consumer(RunnerEvent value);\n");

        var configuration = new SyncConfiguration
        {
            Renames = [new RenameRule("Unload.", "IIU.")],
            NamespaceMappings = [new NamespaceMappingRule("RunnerEvent", "Unload.Core", "IIU.Core.Domain")]
        };

        var plan = workspace.CreatePlan(configuration);

        Assert.Empty(plan.Items);
        Assert.Equal(2, plan.SameCount);
    }

    private static SyncConfiguration CreateConfiguration() => new()
    {
        Renames = [new RenameRule("Unload.", "IIU.")],
        Protected = ["**/appsettings.Production.json"],
        TransformTextIn = ["**/*.cs", "**/*.json"]
    };
}

internal sealed class TemporaryWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"unload-project-sync-tests-{Guid.NewGuid():N}");

    public TemporaryWorkspace()
    {
        SourceRoot = Path.Combine(_root, "source");
        TargetRoot = Path.Combine(_root, "target");
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(TargetRoot);
    }

    public string SourceRoot { get; }

    public string TargetRoot { get; }

    public void WriteSource(string relativePath, string content) => Write(SourceRoot, relativePath, content);

    public void WriteTarget(string relativePath, string content) => Write(TargetRoot, relativePath, content);

    public string ReadTarget(string relativePath) => File.ReadAllText(Resolve(TargetRoot, relativePath));

    public bool TargetExists(string relativePath) => File.Exists(Resolve(TargetRoot, relativePath));

    public SyncPlan CreatePlan(SyncConfiguration configuration) =>
        new SyncPlanner(new GlobMatcher(), new TextFileTransformer())
            .CreatePlan(SourceRoot, TargetRoot, configuration);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Resolve(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Resolve(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
