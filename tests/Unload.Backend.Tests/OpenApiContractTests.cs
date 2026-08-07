using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Unload.Backend.Tests;

public class OpenApiContractTests
{
    [Fact]
    public async Task Committed_schema_matches_the_current_api()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["OpenApiGenerationOnly"] = "true"
                    });
                });
            });
        using var client = factory.CreateClient();

        var actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(FindCommittedSchema()));

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            "openapi/Unload.Api.json is stale. Run tools/export-openapi.sh and npm run generate:api.");
    }

    private static string FindCommittedSchema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "openapi", "Unload.Api.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not find openapi/Unload.Api.json from the test output directory.");
    }
}
