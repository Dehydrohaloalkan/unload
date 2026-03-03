namespace Unload.Console.CatalogSelection;

/// <summary>
/// Группа target-выборок для интерактивного выбора в консоли.
/// Используется UI-слоем консольного приложения при построении меню выбора.
/// </summary>
/// <param name="GroupId">Идентификатор группы каталога.</param>
/// <param name="GroupName">Отображаемое имя группы.</param>
/// <param name="Targets">Target-выборки, доступные для выбора в группе.</param>
internal record CatalogSelectionGroup(
    int GroupId,
    string GroupName,
    IReadOnlyList<CatalogSelectionTarget> Targets);

/// <summary>
/// Target-выборка для интерактивного выбора в консольном UI.
/// </summary>
/// <param name="TargetCode">Target-код в формате <c>GROUP_MEMBER</c>.</param>
/// <param name="MemberName">Имя участника target-выборки.</param>
/// <param name="MemberCode">Код участника target-выборки.</param>
internal record CatalogSelectionTarget(
    string TargetCode,
    string MemberName,
    string MemberCode);
