using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Web;

namespace Reporting.Api.Common;

/// <summary>
/// Service-to-service authentication for <c>/internal/**</c> (projection feeds): callers send the shared key
/// configured as <c>InternalApi:ApiKey</c> (env <c>InternalApi__ApiKey</c>, ≥ 32 characters) in the
/// <c>X-Internal-Api-Key</c> header. The gateway has no route to <c>/internal/**</c>, so only services
/// inside the compose network can reach these endpoints. Missing configuration rejects every call.
/// </summary>
public sealed class InternalApiKeyFilter(IConfiguration configuration, ILogger<InternalApiKeyFilter> logger)
    : IEndpointFilter
{
    public const string HeaderName = "X-Internal-Api-Key";
    public const string ConfigurationKey = "InternalApi:ApiKey";
    public const int MinimumKeyLength = 32;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration[ConfigurationKey];
        if (string.IsNullOrEmpty(expected) || expected.Length < MinimumKeyLength)
        {
            logger.LogWarning(
                "{ConfigurationKey} is not configured (or shorter than {MinimumKeyLength}); internal call rejected.",
                ConfigurationKey,
                MinimumKeyLength);
            return Reject(context.HttpContext);
        }

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!FixedTimeEquals(presented, expected))
        {
            return Reject(context.HttpContext);
        }

        return await next(context);
    }

    private static bool FixedTimeEquals(string presented, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static IResult Reject(HttpContext httpContext) =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Internal authentication required",
            detail: "A valid internal API key is required.",
            type: "https://api.grottoworks.example/problems/internal-auth-required",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "INTERNAL_AUTH_REQUIRED",
                ["correlationId"] = CorrelationIdMiddlewareExtensions.GetCorrelationId(httpContext)
            });
}
