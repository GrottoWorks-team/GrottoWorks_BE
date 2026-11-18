using System.Text.Json;
using BuildingBlocks.Web;
using Xunit;

namespace Platform.UnitTests;

public sealed class GatewaySwaggerTests
{
    [Theory]
    [InlineData("/internal", true)]
    [InlineData("internal/users", true)]
    [InlineData("/INTERNAL/system-export", true)]
    [InlineData("/internality", false)]
    [InlineData("api/v1/internal", false)]
    [InlineData(null, false)]
    public void Internal_paths_are_recognized_in_requests_and_api_descriptions(string? path, bool expected)
    {
        Assert.Equal(expected, PublicApiPaths.IsInternal(path));
    }

    [Fact]
    public void Swagger_proxies_only_the_seven_fixed_openapi_documents()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GrottoWorks_BE.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        using var settings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(directory.FullName, "ApiGateway/YarpApiGateway/appsettings.json")));
        var swagger = settings.RootElement.GetProperty("Swagger");
        var documents = swagger.GetProperty("Documents");
        var routes = settings.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        string[] clusters = ["identity", "parish", "resource", "task", "readiness", "reporting", "notificationaudit"];

        Assert.True(swagger.GetProperty("Enabled").GetBoolean());
        Assert.Equal(clusters.Length, documents.EnumerateObject().Count());
        Assert.Equal(clusters.Length, routes.EnumerateObject().Count(route =>
            route.Value.GetProperty("Match").GetProperty("Path").GetString()!
                .StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)));

        foreach (var cluster in clusters)
        {
            Assert.False(string.IsNullOrWhiteSpace(documents.GetProperty(cluster).GetString()));
            var route = routes.GetProperty($"swagger-{cluster}");
            Assert.Equal(cluster, route.GetProperty("ClusterId").GetString());
            Assert.Equal("anonymous", route.GetProperty("AuthorizationPolicy").GetString());

            var match = route.GetProperty("Match");
            Assert.Equal($"/swagger/openapi/{cluster}.json", match.GetProperty("Path").GetString());
            Assert.Equal("GET", Assert.Single(match.GetProperty("Methods").EnumerateArray()).GetString());
            var transform = Assert.Single(route.GetProperty("Transforms").EnumerateArray());
            Assert.Equal("/openapi/v1.json", transform.GetProperty("PathSet").GetString());
        }
    }
}
