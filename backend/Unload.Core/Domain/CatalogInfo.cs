namespace Unload.Core;

public record CatalogInfo(
    IReadOnlyList<CatalogGroupInfo> Groups,
    IReadOnlyList<CatalogMemberInfo> Members,
    IReadOnlyList<CatalogProfileInfo> Profiles);

public record CatalogGroupInfo(
    int Id,
    string Name,
    string Folder);

public record CatalogMemberInfo(
    int Id,
    string Name,
    string Code,
    string FileExtension);

public record CatalogProfileInfo(
    string ProfileCode,
    int GroupId,
    int MemberId,
    string GroupName,
    string GroupFolder,
    string MemberName,
    string MemberCode,
    string MemberFileExtension);
