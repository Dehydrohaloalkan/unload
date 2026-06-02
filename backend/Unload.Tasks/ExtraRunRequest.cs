namespace Unload.Tasks;

/// <summary>
/// Payload запуска extra-выгрузки, передаваемый из <c>ExtraUnloadTask</c> в фоновый движок
/// через <see cref="ExtraActivationChannel"/>. Аналог <c>RunRequest</c> для main-выгрузки.
/// </summary>
/// <param name="CorrelationId">Идентификатор запуска (префикс <c>extra-</c>).</param>
/// <param name="ScriptPaths">Полные пути SQL-скриптов к выполнению (уже отобраны/отсортированы).</param>
/// <param name="BanksFilter">
/// Готовая подстановка для плейсхолдера <c>{banks}</c> в atomic-скриптах
/// (например <c>'B01','B02'</c>); <c>null</c> — выбраны все банки, atomic не используется.
/// </param>
/// <param name="PublishToGateway">Публиковать ли результаты в шлюз.</param>
public record ExtraRunRequest(
    string CorrelationId,
    IReadOnlyList<string> ScriptPaths,
    string? BanksFilter,
    bool PublishToGateway);
