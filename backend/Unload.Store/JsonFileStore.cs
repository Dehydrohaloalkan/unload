using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Unload.Store;

/// <summary>
/// Универсальный хелпер JSON-персистентности с атомарной заменой, резервной копией
/// последнего корректного снимка и карантином повреждённых файлов.
/// После невосстановимого сбоя блокирует следующие записи, сохраняя чтение доступным.
/// </summary>
/// <typeparam name="T">Тип сериализуемого снимка.</typeparam>
public sealed class JsonFileStore<T>
{
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger? _logger;
    private readonly object _sync = new();
    private PersistenceHealthStatus _healthStatus = PersistenceHealthStatus.Healthy;
    private DateTimeOffset? _healthChangedAt;
    private Exception? _persistenceFailure;

    public JsonFileStore(string filePath, JsonSerializerOptions jsonOptions, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _filePath = filePath;
        _backupPath = $"{filePath}.bak";
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    /// <summary>
    /// Загружает основной снимок или восстанавливает его из резервной копии.
    /// Повреждённые файлы перемещаются в карантин и не перезаписываются пустым состоянием.
    /// </summary>
    public T? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return File.Exists(_backupPath)
                    ? RecoverFromBackup(primaryFailure: null)
                    : default;
            }

            if (TryLoadFile(_filePath, out var value, out var failure))
            {
                return value;
            }

            _logger?.LogWarning(failure, "Failed to load store from '{FilePath}'.", _filePath);
            if (!TryQuarantine(_filePath, failure!))
            {
                SetFailure(PersistenceHealthStatus.Corrupted, failure!);
                return default;
            }

            return RecoverFromBackup(failure);
        }
    }

    /// <summary>
    /// Атомарно записывает снимок в JSON-файл (через temp-файл + File.Move).
    /// </summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            ThrowIfPersistenceUnavailable();
            var tempPath = $"{_filePath}.tmp";
            var backupTempPath = $"{_backupPath}.tmp";
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(value, _jsonOptions);
                File.WriteAllText(tempPath, json);
                var targetExists = File.Exists(_filePath);
                if (targetExists)
                {
                    File.Copy(_filePath, backupTempPath, overwrite: true);
                    File.Move(backupTempPath, _backupPath, overwrite: true);
                }

                File.Move(tempPath, _filePath, overwrite: true);
                if (!targetExists)
                {
                    File.Copy(_filePath, backupTempPath, overwrite: true);
                    File.Move(backupTempPath, _backupPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                SetFailure(PersistenceHealthStatus.Degraded, ex);
                _logger?.LogError(ex, "Failed to save store to '{FilePath}'.", _filePath);
                throw;
            }
            finally
            {
                TryDeleteTempFile(tempPath);
                TryDeleteTempFile(backupTempPath);
            }
        }
    }

    public void EnsureWritable()
    {
        lock (_sync)
        {
            ThrowIfPersistenceUnavailable();
        }
    }

    public PersistenceHealthInfo GetHealth()
    {
        lock (_sync)
        {
            return new PersistenceHealthInfo(
                _healthStatus,
                _persistenceFailure is null,
                _healthChangedAt,
                _persistenceFailure?.GetType().Name);
        }
    }

    private T? RecoverFromBackup(Exception? primaryFailure)
    {
        if (!File.Exists(_backupPath))
        {
            SetFailure(
                PersistenceHealthStatus.Corrupted,
                primaryFailure ?? new FileNotFoundException("Persistence primary and backup files are missing."));
            return default;
        }

        if (!TryLoadFile(_backupPath, out var backup, out var backupFailure))
        {
            _logger?.LogWarning(backupFailure, "Failed to load persistence backup from '{FilePath}'.", _backupPath);
            TryQuarantine(_backupPath, backupFailure!);
            SetFailure(PersistenceHealthStatus.Corrupted, backupFailure!);
            return default;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(_backupPath, _filePath, overwrite: true);
            _healthStatus = PersistenceHealthStatus.Recovered;
            _healthChangedAt = DateTimeOffset.UtcNow;
            _persistenceFailure = null;
            _logger?.LogWarning(
                primaryFailure,
                "Persistence store '{FilePath}' was recovered from backup.",
                _filePath);
            return backup;
        }
        catch (Exception ex)
        {
            SetFailure(PersistenceHealthStatus.Degraded, ex);
            _logger?.LogError(ex, "Failed to restore persistence store '{FilePath}' from backup.", _filePath);
            return backup;
        }
    }

    private bool TryLoadFile(string path, out T? value, out Exception? failure)
    {
        try
        {
            var json = File.ReadAllText(path);
            value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            if (value is null)
            {
                throw new JsonException("Persistence snapshot contains JSON null.");
            }

            failure = null;
            return true;
        }
        catch (Exception ex)
        {
            value = default;
            failure = ex;
            return false;
        }
    }

    private bool TryQuarantine(string path, Exception failure)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            var quarantinePath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            File.Move(path, quarantinePath);
            _logger?.LogWarning(
                failure,
                "Corrupted persistence file '{FilePath}' was moved to quarantine.",
                path);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to quarantine corrupted persistence file '{FilePath}'.", path);
            return false;
        }
    }

    private void ThrowIfPersistenceUnavailable()
    {
        if (_persistenceFailure is not null)
        {
            throw new PersistenceUnavailableException(_filePath, _persistenceFailure);
        }
    }

    private void SetFailure(PersistenceHealthStatus status, Exception failure)
    {
        _healthStatus = status;
        _healthChangedAt = DateTimeOffset.UtcNow;
        _persistenceFailure = failure;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
