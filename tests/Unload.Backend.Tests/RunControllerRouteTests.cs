using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Unload.Api.Controllers;

namespace Unload.Backend.Tests;

public class RunControllerRouteTests
{
    [Fact]
    public void SplitControllers_PreserveExistingRunRoutes()
    {
        var controllerTypes = new[]
        {
            typeof(RunLaunchController),
            typeof(RunStatusController),
            typeof(RunHistoryController),
            typeof(GatewayRequeueController)
        };

        var actualRoutes = controllerTypes
            .SelectMany(GetRoutes)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedRoutes = new[]
        {
            "GET api/runs",
            "GET api/runs/{correlationId}",
            "GET api/runs/active",
            "GET api/runs/dashboard",
            "GET api/runs/extra/banks",
            "GET api/runs/history",
            "GET api/runs/preset/state",
            "GET api/runs/today",
            "POST api/runs",
            "POST api/runs/{correlationId}/stop",
            "POST api/runs/extra",
            "POST api/runs/preset",
            "POST api/runs/requeue"
        }.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expectedRoutes, actualRoutes);
    }

    private static IEnumerable<string> GetRoutes(Type controllerType)
    {
        var controllerRoute = controllerType.GetCustomAttribute<RouteAttribute>()?.Template;
        Assert.False(string.IsNullOrWhiteSpace(controllerRoute));

        return controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.HttpMethods.Select(method =>
                $"{method} {CombineRoute(controllerRoute!, attribute.Template)}"));
    }

    private static string CombineRoute(string controllerRoute, string? actionRoute)
    {
        return string.IsNullOrWhiteSpace(actionRoute)
            ? controllerRoute
            : $"{controllerRoute.TrimEnd('/')}/{actionRoute.TrimStart('/')}";
    }
}
