using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Unload.Store;

/// <summary>
/// Универсальный хелпер атомарной JSON-персистентности (запись через temp-файл + File.Move).
/// Загрузка: при ошибке/отсутствии файла возвращает default(T) с предупреждением в лог.
/// Запись: сбой персистентности логируется как Error (молчаливая потеря состояния недопустима),
/// поэтому хранилищам важно передавать реальный <see cref="ILogger"/>.
/// </summary>
/// <typeparam name="T">Тип сериализуемого снимка.</typeparam>
public sealed class JsonFileStore<T>
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger? _logger;
    private readonly object _sync = new();

    public JsonFileStore(string filePath, JsonSerializerOptions jsonOptions, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _filePath = filePath;
        _jsonOptions = jsonOptions;
        _logger = logger;
    }

    /// <summary>
    /// Загружает снимок из JSON-файла. При отсутствии файла или ошибке возвращает <c>default(T)</c>.
    /// </summary>
    public T? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return default;
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load store from '{FilePath}'.", _filePath);
            return default;
        }
    }

    /// <summary>
    /// Атомарно записывает снимок в JSON-файл (через temp-файл + File.Move).
    /// </summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(value, _jsonOptions);
                var tempPath = $"{_filePath}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            // Потеря состояния «источника правды» — это не warning: запуски/история
            // не переживут рестарт. Логируем как Error, чтобы сбой был заметен в мониторинге.
            _logger?.LogError(ex, "Failed to save store to '{FilePath}'.", _filePath);
        }
    }
}
