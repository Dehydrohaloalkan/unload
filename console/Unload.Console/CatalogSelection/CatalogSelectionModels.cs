namespace Unload.Console.CatalogSelection;

/// <summary>
/// Группа профилей для интерактивного выбора в консоли.
/// Используется UI-слоем консольного приложения при построении меню выбора.
/// </summary>
/// <param name="GroupId">Идентификатор группы каталога.</param>
/// <param name="GroupName">Отображаемое имя группы.</param>
/// <param name="Profiles">Профили, доступные для выбора в группе.</param>
internal record CatalogSelectionGroup(
    int GroupId,
    string GroupName,
    IReadOnlyList<CatalogSelectionProfile> Profiles);

/// <summary>
/// Профиль для интерактивного выбора в консольном UI.
/// </summary>
/// <param name="ProfileCode">Код профиля в формате <c>GROUP_MEMBER</c>.</param>
/// <param name="MemberName">Имя участника профиля.</param>
/// <param name="MemberCode">Код участника профиля.</param>
internal record CatalogSelectionProfile(
    string ProfileCode,
    string MemberName,
    string MemberCode);
