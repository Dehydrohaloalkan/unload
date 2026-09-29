using Microsoft.AspNetCore.Mvc;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Core;

namespace Unload.Api.Controllers;

/// <summary>
/// Управляет runtime-паролем БД. Пароль существует только в памяти backend.
/// </summary>
[ApiController]
[Route("api/database")]
public sealed class DatabaseController(
    IDatabaseCredentialStore credentialStore,
    IDatabaseClientFactory databaseClientFactory,
    ILogger<DatabaseController> logger) : ControllerBase
{
    private readonly IDatabaseCredentialStore _credentialStore = credentialStore;
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly ILogger<DatabaseController> _logger = logger;

    [HttpGet("status")]
    public ActionResult<DatabaseCredentialStatusResponse> GetStatus()
    {
        return Ok(CreateStatusResponse());
    }

    [HttpPost("connect")]
    [ProducesResponseType<DatabaseCredentialStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DatabaseCredentialStatusResponse>> Connect(
        [FromBody] SetDatabaseCredentialsRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.DatabaseId) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrEmpty(request.Password))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Database, username and password are required.",
                "VALIDATION_ERROR");
        }

        try
        {
            _credentialStore.SetCredentials(request.DatabaseId, request.Username, request.Password);
        }
        catch (ArgumentException)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Selected database is not configured.",
                "VALIDATION_ERROR");
        }
        try
        {
            var client = _databaseClientFactory.CreateClient();
            try
            {
                if (!client.IsConnected)
                {
                    throw new InvalidOperationException("Database connection is not available.");
                }
            }
            finally
            {
                await DisposeClientAsync(client);
            }
        }
        catch
        {
            _credentialStore.Clear();
            _logger.LogWarning("Database password validation failed.");
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                "Database connection failed",
                "Не удалось подключиться к базе данных. Проверьте пароль.",
                "DATABASE_CONNECTION_FAILED");
        }

        _logger.LogInformation("Database credentials configured for the current process.");
        return Ok(CreateStatusResponse());
    }

    private DatabaseCredentialStatusResponse CreateStatusResponse()
    {
        return new DatabaseCredentialStatusResponse(
            _credentialStore.IsConfigured,
            _credentialStore.SelectedDatabaseId,
            _credentialStore.Databases
                .Select(static database => new DatabaseOptionResponse(database.Id, database.Name))
                .ToArray());
    }

    private static async ValueTask DisposeClientAsync(IDatabaseClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
