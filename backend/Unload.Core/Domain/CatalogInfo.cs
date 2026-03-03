namespace Unload.Core;

/// <summary>
/// Нормализованная модель каталога профилей, собираемая из <c>configs/catalog.json</c>.
/// Используется API для выдачи каталога и раннером для резолва профилей в скрипты.
/// </summary>
/// <param name="Groups">Список групп каталога.</param>
/// <param name="Members">Список участников/подсистем каталога.</param>
/// <param name="Profiles">Список вычисленных профилей вида <c>GROUP_MEMBER</c>.</param>
public record CatalogInfo(
    IReadOnlyList<CatalogGroupInfo> Groups,
    IReadOnlyList<CatalogMemberInfo> Members,
    IReadOnlyList<CatalogProfileInfo> Profiles);

/// <summary>
/// Описание группы в каталоге.
/// Используется для построения профиля и поиска папки со скриптами.
/// </summary>
/// <param name="Id">Уникальный идентификатор группы из каталога.</param>
/// <param name="Name">Отображаемое имя группы.</param>
/// <param name="Folder">Имя папки группы в директории <c>scripts</c>.</param>
public record CatalogGroupInfo(
    int Id,
    string Name,
    string Folder);

/// <summary>
/// Описание участника каталога.
/// Используется для построения кода профиля и расширения итогового файла.
/// </summary>
/// <param name="Id">Уникальный идентификатор участника.</param>
/// <param name="Name">Отображаемое имя участника.</param>
/// <param name="Code">Короткий код участника.</param>
/// <param name="FileExtension">Расширение файла результата для участника.</param>
public record CatalogMemberInfo(
    int Id,
    string Name,
    string Code,
    string FileExtension);

/// <summary>
/// Связка группы и участника, представляющая конкретный профиль выгрузки.
/// Используется сервисом каталога при резолве профилей в SQL-скрипты.
/// </summary>
/// <param name="ProfileCode">Полный код профиля в формате <c>GROUP_MEMBER</c>.</param>
/// <param name="GroupId">Идентификатор группы профиля.</param>
/// <param name="MemberId">Идентификатор участника профиля.</param>
/// <param name="GroupName">Имя группы профиля.</param>
/// <param name="GroupFolder">Папка группы профиля в <c>scripts</c>.</param>
/// <param name="MemberName">Имя участника профиля.</param>
/// <param name="MemberCode">Код участника профиля.</param>
/// <param name="MemberFileExtension">Расширение итоговых файлов для профиля.</param>
public record CatalogProfileInfo(
    string ProfileCode,
    int GroupId,
    int MemberId,
    string GroupName,
    string GroupFolder,
    string MemberName,
    string MemberCode,
    string MemberFileExtension);
