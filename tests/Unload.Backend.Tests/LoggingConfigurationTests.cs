using System.Xml.Linq;

namespace Unload.Backend.Tests;

public class LoggingConfigurationTests
{
    private static readonly XNamespace NLogNamespace = "http://www.nlog-project.org/schemas/NLog.xsd";

    [Fact]
    public void ApiFileTarget_RendersMessagesAndKeepsSearchableContextColumns()
    {
        var document = XDocument.Load(FindRepositoryFile("backend", "Unload.Api", "nlog.config"));
        var fileTarget = document
            .Descendants(NLogNamespace + "target")
            .Single(element => (string?)element.Attribute("name") == "api_file");
        var columns = fileTarget
            .Descendants(NLogNamespace + "column")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string)element.Attribute("layout")!,
                StringComparer.Ordinal);

        Assert.Equal("${basedir}/logs/api-v2-${shortdate}.csv", (string?)fileTarget.Attribute("fileName"));
        Assert.Equal("${message}", columns["message"]);
        Assert.Equal("${event-properties:item=CorrelationId}", columns["correlationId"]);
        Assert.Equal("${event-properties:item=TaskCode}", columns["taskCode"]);
        Assert.Equal("${event-properties:item=BatchId}", columns["batchId"]);
    }

    [Fact]
    public void LoggingRules_KeepLifecycleMessagesAndSuppressFrameworkInformationNoise()
    {
        var document = XDocument.Load(FindRepositoryFile("backend", "Unload.Api", "nlog.config"));
        var rules = document
            .Descendants(NLogNamespace + "logger")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                StringComparer.Ordinal);

        var lifetimeRule = rules["Microsoft.Hosting.Lifetime"];
        Assert.Equal("Info", (string?)lifetimeRule.Attribute("minlevel"));
        Assert.Equal("true", (string?)lifetimeRule.Attribute("final"));

        var frameworkRule = rules["Microsoft.*"];
        Assert.Equal("Info", (string?)frameworkRule.Attribute("maxlevel"));
        Assert.Equal("true", (string?)frameworkRule.Attribute("final"));
        Assert.Null(frameworkRule.Attribute("writeTo"));

        Assert.Equal("Info", (string?)rules["*"].Attribute("minlevel"));
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "unload.slnx")))
            {
                continue;
            }

            return Path.Combine([directory.FullName, .. pathParts]);
        }

        throw new DirectoryNotFoundException("Could not locate the Unload repository root.");
    }
}
