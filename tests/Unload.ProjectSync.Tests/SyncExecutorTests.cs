using Unload.ProjectSync;

namespace Unload.ProjectSync.Tests;

public sealed class SyncExecutorTests
{
    [Fact]
    public void Execute_AppliesOnlySelectedFilesAndBacksUpUpdatedTarget()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.WriteSource("backend/Unload.Api/Existing.cs", "namespace Unload.Api; // new\n");
        workspace.WriteSource("backend/Unload.Api/Selected.cs", "namespace Unload.Api;\n");
        workspace.WriteSource("backend/Unload.Api/NotSelected.cs", "namespace Unload.Api;\n");
        workspace.WriteTarget("backend/IIU.Api/Existing.cs", "namespace IIU.Api; // old\n");

        var configuration = new SyncConfiguration
        {
            Renames = [new RenameRule("Unload.", "IIU.")],
            TransformTextIn = ["**/*.cs"]
        };
        var plan = workspace.CreatePlan(configuration);
        var selected = plan.ApplicableItems
            .Where(static item =>
                item.TargetRelativePath is "backend/IIU.Api/Existing.cs" or "backend/IIU.Api/Selected.cs")
            .ToArray();

        var result = new SyncExecutor(new TextFileTransformer()).Execute(plan, selected, configuration);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.BackupCount);
        Assert.Equal("namespace IIU.Api; // new\n", workspace.ReadTarget("backend/IIU.Api/Existing.cs"));
        Assert.Equal("namespace IIU.Api;\n", workspace.ReadTarget("backend/IIU.Api/Selected.cs"));
        Assert.False(workspace.TargetExists("backend/IIU.Api/NotSelected.cs"));
        Assert.NotNull(result.BackupDirectory);
        Assert.Equal(
            "namespace IIU.Api; // old\n",
            File.ReadAllText(Path.Combine(result.BackupDirectory!, "backend", "IIU.Api", "Existing.cs")));
    }
}
