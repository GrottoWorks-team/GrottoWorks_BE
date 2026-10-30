using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Web;

namespace NotificationAudit.Api.Common;

public sealed class InternalApiKeyFilter(IConfiguration configuration, ILogger<InternalApiKeyFilter> logger)
    : IEndpointFilter
{
    public const string HeaderName = "X-Internal-Api-Key";
    private const string ConfigurationKey = "InternalApi:ApiKey";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration[ConfigurationKey];
        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(expected) || expected.Length < 32 || !FixedTimeEquals(presented, expected))
        {
            logger.LogWarning("Rejected an internal file lookup with invalid service credentials.");
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Internal authentication required",
                detail: "A valid internal API key is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "INTERNAL_AUTH_REQUIRED",
                    ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(context.HttpContext),
                    ["traceId"] = context.HttpContext.TraceIdentifier
                });
        }

        return await next(context);
    }

    private static bool FixedTimeEquals(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
