namespace Unload.Api.Models;

/// <summary>
/// Контракт ответа со служебным серверным временем API.
/// Используется frontend-клиентами для синхронизации часов с backend.
/// </summary>
/// <param name="ServerLocalTime">Текущее локальное время сервера.</param>
/// <param name="ServerUtcTime">Текущее UTC-время сервера.</param>
/// <param name="UtcOffsetMinutes">Смещение локального времени сервера относительно UTC в минутах.</param>
/// <param name="TimeZoneId">Идентификатор локальной таймзоны сервера.</param>
public  record ServerTimeResponse(
    DateTimeOffset ServerLocalTime,
    DateTimeOffset ServerUtcTime,
    int UtcOffsetMinutes,
    string TimeZoneId);

public  record OutputFileInfo(
    string FileName,
    string FilePath,
    DateTimeOffset ModifiedAt,
    long SizeBytes);
