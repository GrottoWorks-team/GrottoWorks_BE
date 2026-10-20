using System.Text.Json;
using FluentAssertions;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskGatewayRouteTests
{
    [Fact]
    public void Completion_criteria_route_targets_task_cluster_with_high_priority()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .GetProperty("task-completion-criteria-route");

        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("Order").GetInt32().Should().Be(-1);
        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/v1/tasks/{taskId}/completion-criteria");
    }

    [Fact]
    public void Task_skills_route_targets_task_cluster_with_high_priority()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .GetProperty("task-skills-route");

        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("Order").GetInt32().Should().Be(-1);
        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/v1/tasks/{taskId}/skills");
    }

    private static string GetGatewaySettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GrottoWorks_BE.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        return Path.Combine(directory!.FullName, "ApiGateway", "YarpApiGateway", "appsettings.json");
    }
}
