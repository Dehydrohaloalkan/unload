using Unload.Store;

namespace Unload.Backend.Tests;

public class RunTaskCodeResolverTests
{
    [Theory]
    [InlineData("extra-123", "extra")]
    [InlineData(" EXTRA-123 ", "extra")]
    [InlineData("preset-123", "preset")]
    [InlineData("run-123", "run")]
    [InlineData("unknown", "run")]
    public void Resolve_UsesKnownPrefixOrRunFallback(string correlationId, string expectedTaskCode)
    {
        Assert.Equal(expectedTaskCode, RunTaskCodeResolver.Resolve(correlationId));
    }
}
