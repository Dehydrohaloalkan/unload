using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Unload.Api.Controllers;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Core;
using Unload.DataBase;

namespace Unload.Backend.Tests;

public class DatabaseControllerTests
{
    [Fact]
    public async Task Connect_ValidConnectionKeepsCredentialsInRuntimeStore()
    {
        var store = CreateStore();
        var controller = new DatabaseController(
            store,
            new TestDatabaseClientFactory(isConnected: true),
            NullLogger<DatabaseController>.Instance);

        var result = Assert.IsType<OkObjectResult>(
            (await controller.Connect(new SetDatabaseCredentialsRequest("main", "operator", "secret"))).Result);

        Assert.True(Assert.IsType<DatabaseCredentialStatusResponse>(result.Value).Configured);
        Assert.True(store.IsConfigured);
        Assert.Equal("main", store.SelectedDatabaseId);
    }

    [Fact]
    public async Task Connect_FailedConnectionClearsCredentialsAndReturnsSafeError()
    {
        var store = CreateStore();
        var controller = new DatabaseController(
            store,
            new TestDatabaseClientFactory(isConnected: false),
            NullLogger<DatabaseController>.Instance);

        var exception = await Assert.ThrowsAsync<ApiProblemException>(() =>
            controller.Connect(new SetDatabaseCredentialsRequest("main", "operator", "top-secret")));

        Assert.Equal("DATABASE_CONNECTION_FAILED", exception.ErrorCode);
        Assert.DoesNotContain("top-secret", exception.Message, StringComparison.Ordinal);
        Assert.False(store.IsConfigured);
    }

    private static DatabaseCredentialStore CreateStore()
    {
        return new DatabaseCredentialStore(
        [
            new DatabaseConnectionDefinition("main", "Основная", "Server=db;Database=unload;"),
            new DatabaseConnectionDefinition("archive", "Архив", "Server=db;Database=archive;"),
        ]);
    }

    private sealed class TestDatabaseClientFactory(bool isConnected) : IDatabaseClientFactory
    {
        public IDatabaseClient CreateClient() => new TestDatabaseClient(isConnected);
    }

    private sealed class TestDatabaseClient(bool isConnected) : IDatabaseClient
    {
        public bool IsConnected { get; } = isConnected;

        public Task<System.Data.Common.DbDataReader> GetDataReaderAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
