using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Web.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TaskExecution.Api.Endpoints;
using TaskExecution.UnitTests.Application;

namespace TaskExecution.UnitTests.Api;

public sealed class TaskTeamLeaderRouteTests
{
    [Fact]
    public async Task Team_leader_routes_enforce_the_wire_contract_and_return_etags()
    {
        var fixture = new TaskTeamLeaderTests.Fixture();
        var assignment = fixture.AddAssignment(fixture.Source.VolunteerId);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(fixture.Service);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "LEADER")], "test"));
            try { await next(); }
            catch (DomainException exception) { context.Response.StatusCode = exception.StatusCode ?? 409; }
        });
        app.UseAuthorization();
        app.MapTaskTeamLeaderEndpoints();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var path = $"/api/v1/tasks/{fixture.Task.Id}/team-leader";
        var initialETag = $"\"{fixture.Task.Version}\"";
        var validBody = JsonSerializer.Serialize(new { teamLeaderAssignmentId = assignment.Id });

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string? etag, string? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await client.SendAsync(request);
        }

        using var missingHeader = await SendAsync(HttpMethod.Put, null, validBody);
        using var invalidHeader = await SendAsync(HttpMethod.Delete, "*");
        using var extraField = await SendAsync(HttpMethod.Put, initialETag,
            JsonSerializer.Serialize(new { teamLeaderAssignmentId = assignment.Id, version = fixture.Task.Version }));
        using var emptyAssignment = await SendAsync(HttpMethod.Put, initialETag,
            JsonSerializer.Serialize(new { teamLeaderAssignmentId = Guid.Empty }));
        using var unexpectedBody = await SendAsync(HttpMethod.Delete, initialETag, "{}");
        new[] { missingHeader, invalidHeader, extraField, emptyAssignment, unexpectedBody }
            .Should().OnlyContain(response => response.StatusCode == HttpStatusCode.BadRequest);
        fixture.Source.Store.Saves.Should().Be(0);

        using var selected = await SendAsync(HttpMethod.Put, initialETag, validBody);
        selected.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentETag = selected.Headers.ETag!.Tag;
        currentETag.Should().NotBe(initialETag);
        using var json = JsonDocument.Parse(await selected.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetProperty("teamLeaderAssignmentId").GetGuid().Should().Be(assignment.Id);

        using var unchanged = await SendAsync(HttpMethod.Put, currentETag, validBody);
        unchanged.StatusCode.Should().Be(HttpStatusCode.OK);
        unchanged.Headers.ETag!.Tag.Should().Be(currentETag);
        fixture.Source.Store.Saves.Should().Be(1);
    }
}
