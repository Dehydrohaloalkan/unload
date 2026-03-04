namespace Unload.Core;

/// <summary>
/// Нормализованная модель каталога target-выборок, собираемая из <c>configs/catalog.json</c>.
/// Используется API для выдачи каталога и раннером для резолва target-кодов в скрипты.
/// </summary>
/// <param name="Groups">Список групп каталога.</param>
/// <param name="Members">Список участников/подсистем каталога.</param>
/// <param name="Targets">Список вычисленных target-выборок вида <c>GROUP_MEMBER</c>.</param>
public record CatalogInfo(
    IReadOnlyList<CatalogGroupInfo> Groups,
    IReadOnlyList<CatalogMemberInfo> Members,
    IReadOnlyList<CatalogTargetInfo> Targets);

/// <summary>
/// Описание группы в каталоге.
/// Используется для построения target-кода и поиска папки со скриптами.
/// </summary>
/// <param name="Id">Уникальный идентификатор группы из каталога.</param>
/// <param name="Name">Отображаемое имя группы.</param>
/// <param name="Folder">Имя папки группы в директории <c>scripts</c>.</param>
/// <param name="Code">Код группы для шаблона имени SQL-скриптов.</param>
public record CatalogGroupInfo(
    int Id,
    string Name,
    string Folder,
    string Code);

/// <summary>
/// Описание участника каталога.
/// Используется для построения target-кода и расширения итогового файла.
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
/// Связка группы и участника, представляющая конкретную target-выборку выгрузки.
/// Используется сервисом каталога при резолве target-кодов в SQL-скрипты.
/// </summary>
/// <param name="TargetCode">Полный target-код в формате <c>GROUP_MEMBER</c>.</param>
/// <param name="GroupId">Идентификатор группы target-выборки.</param>
/// <param name="MemberId">Идентификатор участника target-выборки.</param>
/// <param name="GroupName">Имя группы target-выборки.</param>
/// <param name="GroupFolder">Папка группы target-выборки в <c>scripts</c>.</param>
/// <param name="GroupCode">Код группы target-выборки в формате имени SQL-скриптов.</param>
/// <param name="MemberName">Имя участника target-выборки.</param>
/// <param name="MemberCode">Код участника target-выборки.</param>
/// <param name="MemberFileExtension">Расширение итоговых файлов для target-выборки.</param>
public record CatalogTargetInfo(
    string TargetCode,
    int GroupId,
    int MemberId,
    string GroupName,
    string GroupFolder,
    string GroupCode,
    string MemberName,
    string MemberCode,
    string MemberFileExtension);
