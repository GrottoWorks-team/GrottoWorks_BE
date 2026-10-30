using System.Text.Json;
using FluentAssertions;

namespace ParishCoordination.UnitTests;

public sealed class ParishGatewayRouteTests
{
    [Fact]
    public void Work_area_progress_is_covered_by_the_parish_work_area_route()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .GetProperty("parish-work-areas-route");

        route.GetProperty("ClusterId").GetString().Should().Be("parish");
        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/v1/work-areas/{**catch-all}");
    }

    private static string GetGatewaySettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GrottoWorks_BE.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        return Path.Combine(
            directory!.FullName,
            "ApiGateway",
            "YarpApiGateway",
            "appsettings.json");
    }
}
