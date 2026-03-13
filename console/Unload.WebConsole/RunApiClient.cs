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
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var message = payload.TryGetProperty("detail", out var detailElement)
                ? detailElement.GetString()
                : "Run is already in progress.";
            var activeCorrelationId = payload.TryGetProperty("activeCorrelationId", out var activeCorrelationElement)
                ? activeCorrelationElement.GetString()
                : null;
            var conflict = new RunConflictResponse(message ?? "Run is already in progress.", activeCorrelationId);
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

    /// <summary>
    /// Возвращает текущее состояние preset-гейта.
    /// </summary>
    public async Task<PresetGateStateDto?> GetPresetStateAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("/api/runs/preset/state", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PresetGateStateDto>(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Запускает preset-задачу на API.
    /// </summary>
    public async Task<ScriptTaskRunResultDto> RunPresetAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("/api/runs/preset", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScriptTaskRunResultDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Preset result payload is empty.");
    }

    /// <summary>
    /// Запускает extra-задачу на API.
    /// </summary>
    public async Task<ScriptTaskRunResultDto> RunExtraAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("/api/runs/extra", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScriptTaskRunResultDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Extra result payload is empty.");
    }
}
