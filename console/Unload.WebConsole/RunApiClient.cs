using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unload.WebConsole;

/// <summary>
/// HTTP-клиент для операций запуска и чтения статуса выгрузки.
/// </summary>
internal sealed class RunApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Пытается запустить выгрузку через API.
    /// </summary>
    /// <param name="memberCodes">Коды выбранных мемберов для запуска.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Результат старта с данными accepted или conflict.</returns>
    public async Task<RunStartResult> StartRunAsync(
        IReadOnlyCollection<string> memberCodes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/runs",
            new RunStartRequest(memberCodes),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<RunConflictResponse>(cancellationToken: cancellationToken);
            return new RunStartResult(null, conflict);
        }

        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync<RunAcceptedResponse>(cancellationToken: cancellationToken);
        return new RunStartResult(accepted, null);
    }

    /// <summary>
    /// Возвращает актуальный статус указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Статус запуска или <c>null</c>, если run не найден.</returns>
    public async Task<RunStatusInfoDto?> GetRunStatusAsync(string correlationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"/api/runs/{Uri.EscapeDataString(correlationId)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RunStatusInfoDto>(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Возвращает список мемберов, доступных для запуска выгрузки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Список мемберов каталога.</returns>
    public async Task<IReadOnlyList<MemberCatalogItemDto>> GetMembersAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/api/members", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MemberCatalogItemDto[]>(cancellationToken: cancellationToken)
            ?? Array.Empty<MemberCatalogItemDto>();
    }

    /// <summary>
    /// Возвращает идентификатор активного запуска из API.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Correlation id активного запуска или <c>null</c>.</returns>
    public async Task<string?> ResolveActiveCorrelationIdAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/api/runs/active", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return payload.TryGetProperty("correlationId", out var correlationIdElement)
            ? correlationIdElement.GetString()
            : null;
    }

    /// <summary>
    /// Отправляет запрос на остановку указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns><c>true</c>, если запрос принят API.</returns>
    public async Task<bool> StopRunAsync(string correlationId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"/api/runs/{Uri.EscapeDataString(correlationId)}/stop",
            content: null,
            cancellationToken);
        return response.StatusCode == HttpStatusCode.Accepted;
    }
}
