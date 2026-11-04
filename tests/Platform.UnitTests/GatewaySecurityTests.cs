using System.Text.Json;
using Xunit;

namespace Platform.UnitTests;

public sealed class GatewaySecurityTests
{
    [Fact]
    public void Internal_routes_are_never_exposed_and_sensitive_jobs_are_authenticated_and_limited()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"GrottoWorks_BE.slnx")))directory=directory.Parent;
        Assert.NotNull(directory);
        using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName,"ApiGateway/YarpApiGateway/appsettings.json")));
        var routes=settings.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        foreach(var route in routes.EnumerateObject())
            Assert.False(route.Value.GetProperty("Match").GetProperty("Path").GetString()!.StartsWith("/internal",StringComparison.OrdinalIgnoreCase));
        foreach(var name in new[] {"notificationaudit-exports-route","notificationaudit-backups-route","notificationaudit-upload-route"})
        {
            var route=routes.GetProperty(name);
            Assert.Equal("authenticated",route.GetProperty("AuthorizationPolicy").GetString());
            Assert.Equal("notificationaudit",route.GetProperty("ClusterId").GetString());
            Assert.True(route.TryGetProperty("RateLimiterPolicy",out _));
        }
        Assert.Equal("task",routes.GetProperty("task-purchase-create-route").GetProperty("ClusterId").GetString());
        Assert.Equal(-1,routes.GetProperty("task-purchase-create-route").GetProperty("Order").GetInt32());
    }
}
