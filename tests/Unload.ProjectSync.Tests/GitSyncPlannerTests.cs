using System.Diagnostics;
using Unload.ProjectSync;

namespace Unload.ProjectSync.Tests;

public sealed class GitSyncPlannerTests
{
    [Fact]
    public void CommitPlan_AppliesAddsUpdatesRenamesAndDeletesButKeepsProtectedFile()
    {
        using var workspace = new TemporaryGitWorkspace();
        workspace.WriteSource("backend/Unload.Api/Changed.cs", "namespace Unload.Api; // old\n");
        workspace.WriteSource("backend/Unload.Api/Deleted.cs", "namespace Unload.Api; // delete\n");
        workspace.WriteSource("backend/Unload.Api/RenameOld.cs", "namespace Unload.Api; // rename\n");
        workspace.WriteSource("backend/Unload.Api/appsettings.Production.json", "{ \"source\": \"old\" }");
        workspace.Commit("initial version");

        workspace.WriteTarget("backend/IIU.Api/Changed.cs", "namespace IIU.Api; // old\n");
        workspace.WriteTarget("backend/IIU.Api/Deleted.cs", "namespace IIU.Api; // delete\n");
        workspace.WriteTarget("backend/IIU.Api/RenameOld.cs", "namespace IIU.Api; // rename\n");
        workspace.WriteTarget("backend/IIU.Api/appsettings.Production.json", "{ \"production\": true }");

        workspace.WriteSource("backend/Unload.Api/Changed.cs", "namespace Unload.Api; // new\n");
        workspace.WriteSource("backend/Unload.Api/Added.cs", "namespace Unload.Api; // add\n");
        workspace.DeleteSource("backend/Unload.Api/Deleted.cs");
        workspace.RenameSource(
            "backend/Unload.Api/RenameOld.cs",
            "backend/Unload.Api/RenameNew.cs");
        workspace.WriteSource("backend/Unload.Api/appsettings.Production.json", "{ \"source\": \"new\" }");
        var targetCommit = workspace.Commit("application changes");

        var configuration = new SyncConfiguration
        {
            Renames = [new RenameRule("Unload.", "IIU.")],
            Protected = ["**/appsettings.Production.json"],
            TransformTextIn = ["**/*.cs", "**/*.json"]
        };
        var textTransformer = new TextFileTransformer();
        var planner = new GitSyncPlanner(new GitClient(), new GlobMatcher(), textTransformer);

        var plan = planner.CreatePlan(
            workspace.SourceRoot,
            workspace.TargetRoot,
            targetCommit,
            configuration);

        Assert.Equal(2, plan.Items.Count(static item => item.Action == SyncAction.Add));
        Assert.Single(plan.Items, static item => item.Action == SyncAction.Update);
        Assert.Equal(2, plan.Items.Count(static item => item.Action == SyncAction.Delete));
        Assert.Single(plan.Items, static item => item.Action == SyncAction.Protected);

        var result = new SyncExecutor(textTransformer).Execute(plan, plan.ApplicableItems, configuration);

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);
        Assert.Equal("namespace IIU.Api; // new\n", workspace.ReadTarget("backend/IIU.Api/Changed.cs"));
        Assert.Equal("namespace IIU.Api; // add\n", workspace.ReadTarget("backend/IIU.Api/Added.cs"));
        Assert.Equal("namespace IIU.Api; // rename\n", workspace.ReadTarget("backend/IIU.Api/RenameNew.cs"));
        Assert.False(workspace.TargetExists("backend/IIU.Api/Deleted.cs"));
        Assert.False(workspace.TargetExists("backend/IIU.Api/RenameOld.cs"));
        Assert.Equal("{ \"production\": true }", workspace.ReadTarget("backend/IIU.Api/appsettings.Production.json"));
    }

}

internal sealed class TemporaryGitWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"unload-project-sync-git-tests-{Guid.NewGuid():N}");

    public TemporaryGitWorkspace()
    {
        SourceRoot = Path.Combine(_root, "source");
        TargetRoot = Path.Combine(_root, "target");
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(TargetRoot);
        RunGit("init", "--initial-branch=main");
        RunGit("config", "user.email", "project-sync-tests@example.invalid");
        RunGit("config", "user.name", "Project Sync Tests");
    }

    public string SourceRoot { get; }

    public string TargetRoot { get; }

    public void WriteSource(string relativePath, string content) => Write(SourceRoot, relativePath, content);

    public void WriteTarget(string relativePath, string content) => Write(TargetRoot, relativePath, content);

    public void DeleteSource(string relativePath) => File.Delete(Resolve(SourceRoot, relativePath));

    public void RenameSource(string oldRelativePath, string newRelativePath)
    {
        var destination = Resolve(SourceRoot, newRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(Resolve(SourceRoot, oldRelativePath), destination);
    }

    public string Commit(string subject)
    {
        RunGit("add", "-A");
        RunGit("commit", "-m", subject);
        return RunGit("rev-parse", "HEAD").Trim();
    }

    public string ReadTarget(string relativePath) => File.ReadAllText(Resolve(TargetRoot, relativePath));

    public bool TargetExists(string relativePath) => File.Exists(Resolve(TargetRoot, relativePath));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            // Git may mark object files as read-only on Windows. The workspace owns every
            // entry below _root, so normalizing attributes here keeps cleanup cross-platform.
            foreach (var path in Directory.EnumerateFileSystemEntries(
                _root,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.SetAttributes(_root, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }

    private string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = SourceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output;
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
