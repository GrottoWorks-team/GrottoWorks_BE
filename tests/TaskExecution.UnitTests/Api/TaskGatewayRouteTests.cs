using System.Text.Json;
using FluentAssertions;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskGatewayRouteTests
{
    [Fact]
    public void Support_response_route_keeps_resource_material_receipt_route_separate()
    {
        using var document=JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var routes=document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        routes.GetProperty("task-support-responses-route").GetProperty("ClusterId").GetString().Should().Be("task");
        routes.GetProperty("resource-support-material-receipt-route").GetProperty("ClusterId").GetString().Should().Be("resource");
        routes.GetProperty("task-support-member-approve-route").GetProperty("ClusterId").GetString().Should().Be("task");
        routes.GetProperty("task-support-member-reject-route").GetProperty("ClusterId").GetString().Should().Be("task");
    }
    [Fact]
    public void Support_publication_routes_only_post_to_task_with_authentication()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("task-support-request-publish-route");
        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("AuthorizationPolicy").GetString().Should().Be("authenticated");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/v1/support-requests/{requestId}/publish");
        route.GetProperty("Match").GetProperty("Methods").EnumerateArray().Select(value => value.GetString()).Should().Equal("POST");
    }
    [Fact]
    public void Task_material_update_routes_to_task_without_overriding_resource_reports()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("task-materials-route");
        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("Order").GetInt32().Should().Be(-1);
        route.GetProperty("Match").GetProperty("Methods").EnumerateArray().Select(item => item.GetString()).Should().Equal("PUT");
    }
    [Fact]
    public void Volunteer_matches_route_targets_task_cluster_with_high_priority()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").GetProperty("task-volunteer-matches-route");
        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("Order").GetInt32().Should().Be(-1);
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/v1/tasks/{taskId}/volunteer-matches");
    }
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

    [Fact]
    public void Ready_task_route_targets_task_cluster_with_high_priority()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetGatewaySettingsPath()));
        var route = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .GetProperty("task-ready-route");

        route.GetProperty("ClusterId").GetString().Should().Be("task");
        route.GetProperty("Order").GetInt32().Should().Be(-1);
        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/v1/tasks/{taskId}/ready");
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
