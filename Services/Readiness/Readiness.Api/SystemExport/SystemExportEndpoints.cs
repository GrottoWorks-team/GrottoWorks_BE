using System.Security.Cryptography;
using System.Text;
using Readiness.Infrastructure.Data;
using Readiness.Infrastructure.SystemExport;

namespace Readiness.Api.SystemExport;

public static class SystemExportEndpoints
{
    public static IEndpointRouteBuilder MapSystemExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/system-export", (ReadinessDbContext db, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Stream(stream => new SystemExportSource(db).WriteAsync(stream, context.RequestAborted), "application/x-ndjson");
        }).AllowAnonymous().ExcludeFromDescription().AddEndpointFilter(async (context, next) =>
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expected = configuration["SystemExport:ApiKey"];
            var presented = context.HttpContext.Request.Headers["X-System-Export-Key"].ToString();
            if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(presented))))
                return Results.Problem(statusCode: 401, title: "Export service authentication required", extensions: new Dictionary<string, object?> { ["code"] = "SYSTEM_EXPORT_AUTH_REQUIRED" });
            return await next(context);
        });
        return app;
    }
}
