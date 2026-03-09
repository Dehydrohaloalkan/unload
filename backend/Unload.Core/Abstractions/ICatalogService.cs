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
    /// Резолвит список target-кодов в набор SQL-скриптов и возвращает множество «больших» target-кодов.
    /// </summary>
    /// <param name="targetCodes">Target-коды, которые нужно обработать.</param>
    /// <param name="cancellationToken">Токен отмены резолва.</param>
    /// <returns>Скрипты по target-кодам и множество target-кодов из <c>bigScripts</c>.</returns>
    Task<(IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>> Scripts, IReadOnlySet<string> BigScriptTargetCodes)> ResolveAsync(
        IReadOnlyCollection<string> targetCodes,
        CancellationToken cancellationToken);
}
