using System.Diagnostics;
using System.Text;

namespace Unload.ProjectSync;

public sealed class GitClient
{
    public string GetRepositoryRoot(string workingDirectory) =>
        RunText(workingDirectory, "rev-parse", "--show-toplevel").Trim();

    public string ResolveCommit(string repositoryRoot, string reference) =>
        RunText(repositoryRoot, "rev-parse", "--verify", $"{reference}^{{commit}}").Trim();

    public GitCommitInfo GetCommit(string repositoryRoot, string reference)
    {
        var output = RunText(
            repositoryRoot,
            "show",
            "-s",
            "--format=%H%x1f%h%x1f%cs%x1f%s",
            reference);
        return ParseCommits(output).Single();
    }

    public IReadOnlyList<GitFileChange> GetChangesForCommit(string repositoryRoot, string commit)
    {
        var resolvedCommit = ResolveCommit(repositoryRoot, commit);
        var parentLine = RunText(
            repositoryRoot,
            "rev-list",
            "--parents",
            "-n1",
            resolvedCommit).Trim();
        var commitAndParents = parentLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        const string emptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        var firstParent = commitAndParents.Length > 1 ? commitAndParents[1] : emptyTree;
        return GetChanges(repositoryRoot, firstParent, resolvedCommit);
    }

    public IReadOnlyList<GitCommitInfo> GetRecentCommits(string repositoryRoot, int count = 20)
    {
        var output = RunText(
            repositoryRoot,
            "log",
            $"-n{count}",
            "--format=%H%x1f%h%x1f%cs%x1f%s");
        return ParseCommits(output);
    }

    private IReadOnlyList<GitFileChange> GetChanges(
        string repositoryRoot,
        string parentCommit,
        string targetCommit)
    {
        var bytes = RunBinary(
            repositoryRoot,
            "diff",
            "--name-status",
            "-z",
            "-M",
            parentCommit,
            targetCommit,
            "--");
        var tokens = Encoding.UTF8.GetString(bytes)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<GitFileChange>();

        for (var index = 0; index < tokens.Length;)
        {
            var statusToken = tokens[index++];
            var status = statusToken[0];
            switch (status)
            {
                case 'A':
                case 'M':
                case 'T':
                    EnsureTokenAvailable(tokens, index, statusToken);
                    result.Add(new GitFileChange(
                        status == 'A' ? GitChangeKind.Add : GitChangeKind.Modify,
                        OldPath: null,
                        NewPath: GlobMatcher.Normalize(tokens[index++])));
                    break;
                case 'D':
                    EnsureTokenAvailable(tokens, index, statusToken);
                    result.Add(new GitFileChange(
                        GitChangeKind.Delete,
                        OldPath: GlobMatcher.Normalize(tokens[index++]),
                        NewPath: null));
                    break;
                case 'R':
                case 'C':
                    EnsureTokenAvailable(tokens, index + 1, statusToken);
                    result.Add(new GitFileChange(
                        GitChangeKind.Rename,
                        OldPath: GlobMatcher.Normalize(tokens[index++]),
                        NewPath: GlobMatcher.Normalize(tokens[index++])));
                    break;
                default:
                    throw new GitCommandException($"Git вернул неподдерживаемый статус файла: {statusToken}");
            }
        }

        return result;
    }

    public byte[] ReadFileAtCommit(string repositoryRoot, string commit, string relativePath) =>
        RunBinary(repositoryRoot, "show", $"{commit}:{relativePath}");

    private static IReadOnlyList<GitCommitInfo> ParseCommits(string output)
    {
        var commits = new List<GitCommitInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\u001f', 4);
            if (fields.Length != 4)
            {
                throw new GitCommandException($"Не удалось разобрать строку git log: {line}");
            }

            commits.Add(new GitCommitInfo(fields[0], fields[1], fields[2], fields[3]));
        }

        return commits;
    }

    private static string RunText(string workingDirectory, params string[] arguments)
    {
        var result = Run(workingDirectory, binaryOutput: false, arguments);
        if (result.ExitCode != 0)
        {
            throw new GitCommandException(result.Error.Trim());
        }

        return Encoding.UTF8.GetString(result.Output);
    }

    private static byte[] RunBinary(string workingDirectory, params string[] arguments)
    {
        var result = Run(workingDirectory, binaryOutput: true, arguments);
        if (result.ExitCode != 0)
        {
            throw new GitCommandException(result.Error.Trim());
        }

        return result.Output;
    }

    private static GitCommandResult Run(string workingDirectory, bool binaryOutput, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = binaryOutput ? null : Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new GitCommandException("Не удалось запустить git. Проверьте, что Git установлен и доступен в PATH.");
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        return new GitCommandResult(process.ExitCode, output.ToArray(), error);
    }

    private static void EnsureTokenAvailable(string[] tokens, int index, string status)
    {
        if (index >= tokens.Length)
        {
            throw new GitCommandException($"Git не вернул путь для статуса {status}.");
        }
    }

    private sealed record GitCommandResult(int ExitCode, byte[] Output, string Error);
}

public sealed record GitCommitInfo(string Hash, string ShortHash, string Date, string Subject);

public sealed record GitFileChange(GitChangeKind Kind, string? OldPath, string? NewPath);

public enum GitChangeKind
{
    Add,
    Modify,
    Delete,
    Rename
}

public sealed class GitCommandException(string message) : Exception(
    string.IsNullOrWhiteSpace(message) ? "Git завершился с ошибкой без сообщения." : message);
