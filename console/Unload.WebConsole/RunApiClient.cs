using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unload.WebConsole;

internal sealed class RunApiClient(HttpClient httpClient)
{
    public async Task<RunStartResult> StartRunAsync(
        IReadOnlyCollection<string> targetCodes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/runs",
            new RunStartRequest(targetCodes),
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
}
