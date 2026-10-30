using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Security;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using NotificationAudit.Api.Common;
using NotificationAudit.Api.Endpoints;
using NotificationAudit.Application;
using NotificationAudit.Infrastructure;
using NotificationAudit.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddValidation();
builder.Services.AddGrottoWorksSecurity(builder.Configuration);
builder.Services.AddGrottoWorksCurrentUser();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
    ProblemDetailsAuthorizationMiddlewareResultHandler>();
builder.Services.AddGrottoWorksWeb();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails += context =>
{
    if (context.ProblemDetails is HttpValidationProblemDetails)
        context.ProblemDetails.Extensions["code"] = "VALIDATION_FAILED";
});
builder.Services.AddApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IReadinessProbe, DatabaseReadinessProbe>();
builder.Services.AddScoped<InternalApiKeyFilter>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
    new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false)));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "NotificationAudit API v1");
        options.RoutePrefix = "swagger";
        options.EnablePersistAuthorization();
    });
}
app.UseGrottoWorksDefaults();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapNotificationAuditEndpoints();
app.MapGrottoWorksHealthEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationAuditDbContext>().Database.MigrateAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Database migration failed; readiness remains unhealthy.");
    }
}
app.Run();
public partial class Program;
