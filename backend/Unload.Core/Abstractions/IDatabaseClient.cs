using System.Data.Common;

namespace Unload.Core;

/// <summary>
/// Контракт клиента базы данных, отдающего поток чтения строк запроса.
/// Используется раннером на этапе выполнения SQL-скриптов.
/// </summary>
public interface IDatabaseClient
{
    /// <summary>
    /// Показывает, готово ли подключение к выполнению запросов.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Выполняет SQL-запрос и возвращает потоковый <see cref="DbDataReader"/>.
    /// </summary>
    /// <param name="query">Текст SQL-запроса.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Открытый ридер для последовательного чтения строк результата.</returns>
    Task<DbDataReader> GetDataReaderAsync(string query, CancellationToken cancellationToken = default);
}
