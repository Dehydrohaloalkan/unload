namespace Unload.Core;

/// <summary>
/// Контракт сервиса каталога профилей и SQL-скриптов.
/// Используется оркестратором и раннером для валидации профилей и получения скриптов.
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Загружает и возвращает каталог профилей в нормализованном виде.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения каталога.</param>
    /// <returns>Модель каталога с группами, участниками и профилями.</returns>
    Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Резолвит список кодов профилей в набор SQL-скриптов для выполнения.
    /// </summary>
    /// <param name="profileCodes">Коды профилей, которые нужно обработать.</param>
    /// <param name="cancellationToken">Токен отмены резолва.</param>
    /// <returns>Словарь профиль -> список определений скриптов.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> profileCodes,
        CancellationToken cancellationToken);
}
