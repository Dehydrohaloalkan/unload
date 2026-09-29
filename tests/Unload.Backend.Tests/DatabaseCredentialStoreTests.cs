using System.Data.Common;
using Unload.DataBase;

namespace Unload.Backend.Tests;

public class DatabaseCredentialStoreTests
{
    [Fact]
    public void BuildConnectionString_UsesSelectedDatabaseAndRuntimeCredentials()
    {
        var store = CreateStore();

        store.SetCredentials("archive", "runtime-user", "secret;value");

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = store.BuildConnectionString(),
        };
        Assert.Equal("archive", builder["database"]);
        Assert.Equal("runtime-user", builder["user id"]);
        Assert.Equal("secret;value", builder["password"]);
        Assert.True(store.IsConfigured);
        Assert.Equal("archive", store.SelectedDatabaseId);
    }

    [Fact]
    public void Clear_RemovesRuntimePassword()
    {
        var store = CreateStore();
        store.SetCredentials("main", "user", "secret");

        store.Clear();

        Assert.False(store.IsConfigured);
        Assert.Null(store.SelectedDatabaseId);
        Assert.Throws<InvalidOperationException>(() => store.BuildConnectionString());
    }

    [Fact]
    public void SetCredentials_RejectsDatabaseOutsideConfiguredList()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() =>
            store.SetCredentials("unknown", "user", "secret"));
    }

    private static DatabaseCredentialStore CreateStore()
    {
        return new DatabaseCredentialStore(
        [
            new DatabaseConnectionDefinition("main", "Основная", "Server=db;Database=main;"),
            new DatabaseConnectionDefinition("archive", "Архив", "Server=db;Database=archive;"),
        ]);
    }
}
