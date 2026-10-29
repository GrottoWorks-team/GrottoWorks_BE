using Readiness.Domain;

namespace Readiness.Api.Common;

/// <summary>API Contract 4.1: <c>If-Match</c> (quoted or not) or body <c>version</c>; both present and different → 412.</summary>
internal static class VersionHeader
{
    public static long? Resolve(long? bodyVersion, HttpContext httpContext, string mismatchCode)
    {
        var ifMatch = httpContext.Request.Headers.IfMatch.ToString().Trim();
        if (ifMatch.Length == 0 || ifMatch == "*")
        {
            return bodyVersion;
        }

        if (ifMatch.StartsWith("W/", StringComparison.Ordinal))
        {
            ifMatch = ifMatch[2..];
        }

        if (!long.TryParse(ifMatch.Trim('"'), out var headerVersion))
        {
            throw new DomainRuleException("INVALID_IF_MATCH", "If-Match must contain the numeric resource version.", DomainErrorKind.BadRequest);
        }

        if (bodyVersion is { } body && body != headerVersion)
        {
            throw new DomainRuleException(mismatchCode, "If-Match and body version disagree.", DomainErrorKind.PreconditionFailed);
        }

        return headerVersion;
    }
}
