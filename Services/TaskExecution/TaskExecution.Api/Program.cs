using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using TaskExecution.Api.Common;
using TaskExecution.Api.Endpoints;
using TaskExecution.Application;
using TaskExecution.Infrastructure;
using TaskExecution.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddValidation();
builder.Services.AddGrottoWorksSecurity(builder.Configuration);
builder.Services.AddGrottoWorksCurrentUser();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
    ProblemDetailsAuthorizationMiddlewareResultHandler>();
builder.Services.AddGrottoWorksWeb();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails += context =>
    {
        if (context.ProblemDetails is HttpValidationProblemDetails)
        {
            context.ProblemDetails.Extensions["code"] = "VALIDATION_FAILED";
        }
    };
});
builder.Services.AddApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TaskExecution.Application.Events.ITaskCorrelationContext, TaskCorrelationContext>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IReadinessProbe, DatabaseReadinessProbe>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "TaskExecution API v1");
        options.RoutePrefix = "swagger";
        options.EnablePersistAuthorization();
    });
}

app.UseGrottoWorksDefaults();
app.UseMiddleware<DomainRuleTranslationMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapTaskExecutionEndpoints();
app.MapGrottoWorksHealthEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TaskExecutionDbContext>().Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(
            exception,
            "Database migration failed. The service stays up; /health/ready reports Unhealthy.");
    }
}

app.Run();

public partial class Program;
