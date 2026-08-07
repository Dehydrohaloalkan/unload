namespace Unload.ProjectSync;

public sealed class SyncExecutor(TextFileTransformer textTransformer)
{
    private readonly TextFileTransformer _textTransformer = textTransformer;

    public SyncExecutionResult Execute(
        SyncPlan plan,
        IReadOnlyCollection<SyncPlanItem> selectedItems,
        SyncConfiguration configuration)
    {
        var invalid = selectedItems.FirstOrDefault(static item => !item.CanApply);
        if (invalid is not null)
        {
            throw new InvalidOperationException($"Действие {invalid.Action} нельзя применить автоматически.");
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupRoot = Path.Combine(plan.TargetRoot, configuration.BackupDirectoryName, timestamp);
        var added = 0;
        var updated = 0;
        var deleted = 0;
        var backupCount = 0;

        foreach (var item in selectedItems)
        {
            if (item.Action != SyncAction.Delete &&
                item.SourceFullPath is null &&
                item.DesiredContent is null)
            {
                throw new InvalidOperationException($"У действия отсутствует исходный файл: {item.TargetRelativePath}");
            }

            var targetDirectory = Path.GetDirectoryName(item.TargetFullPath)
                ?? throw new InvalidOperationException($"Не удалось определить каталог: {item.TargetFullPath}");
            Directory.CreateDirectory(targetDirectory);

            if (File.Exists(item.TargetFullPath))
            {
                var backupPath = Path.Combine(
                    backupRoot,
                    item.TargetRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(item.TargetFullPath, backupPath, overwrite: true);
                backupCount++;
            }

            if (item.Action == SyncAction.Delete)
            {
                if (File.Exists(item.TargetFullPath))
                {
                    File.Delete(item.TargetFullPath);
                    deleted++;
                }

                continue;
            }

            var temporaryPath = item.TargetFullPath + $".project-sync-{Guid.NewGuid():N}.tmp";
            try
            {
                if (item.DesiredContent is not null)
                {
                    File.WriteAllBytes(temporaryPath, item.DesiredContent);
                }
                else if (item.TransformText)
                {
                    var bytes = _textTransformer.ReadAndTransform(item.SourceFullPath!, configuration.Renames);
                    File.WriteAllBytes(temporaryPath, bytes);
                }
                else
                {
                    File.Copy(item.SourceFullPath!, temporaryPath, overwrite: false);
                }

                File.Move(temporaryPath, item.TargetFullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            if (item.Action == SyncAction.Add)
            {
                added++;
            }
            else
            {
                updated++;
            }
        }

        return new SyncExecutionResult(
            Added: added,
            Updated: updated,
            Deleted: deleted,
            BackupCount: backupCount,
            BackupDirectory: backupCount > 0 ? backupRoot : null);
    }
}

public sealed record SyncExecutionResult(
    int Added,
    int Updated,
    int Deleted,
    int BackupCount,
    string? BackupDirectory);
