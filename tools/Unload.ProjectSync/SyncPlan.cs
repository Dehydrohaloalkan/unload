namespace Unload.ProjectSync;

public enum SyncAction
{
    Add,
    Update,
    Delete,
    Protected,
    TargetOnly
}

public sealed record SyncPlanItem(
    SyncAction Action,
    string SourceRelativePath,
    string TargetRelativePath,
    string? SourceFullPath,
    string TargetFullPath,
    bool TransformText,
    byte[]? DesiredContent = null)
{
    public bool CanApply => Action is SyncAction.Add or SyncAction.Update or SyncAction.Delete;
}

public sealed record SyncPlan(
    string SourceRoot,
    string TargetRoot,
    IReadOnlyList<SyncPlanItem> Items,
    int SameCount,
    int IgnoredFileCount)
{
    public IReadOnlyList<SyncPlanItem> ApplicableItems => Items.Where(static item => item.CanApply).ToArray();
}
