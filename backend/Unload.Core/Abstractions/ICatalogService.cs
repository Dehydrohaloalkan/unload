namespace Unload.Core;

/// <summary>
/// Контракт сервиса каталога target-выборок и SQL-скриптов.
/// Используется оркестратором и раннером для валидации target-кодов и получения скриптов.
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Загружает и возвращает каталог target-выборок в нормализованном виде.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения каталога.</param>
    /// <returns>Модель каталога с группами, участниками и target-выборками.</returns>
    Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Резолвит список target-кодов в набор SQL-скриптов для выполнения.
    /// </summary>
    /// <param name="targetCodes">Target-коды, которые нужно обработать.</param>
    /// <param name="cancellationToken">Токен отмены резолва.</param>
    /// <returns>Словарь target-код -> список определений скриптов.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> targetCodes,
        CancellationToken cancellationToken);
}
