using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Exceptions;

/// <summary>Per-service mapping of domain error codes to HTTP statuses (F-PLT-03).</summary>
public sealed class GrottoWorksWebOptions
{
    /// <summary>Error code → HTTP status (+ optional RFC 9457 title) used by the exception middleware.</summary>
    public Dictionary<string, ErrorCodeMapping> ErrorCodes { get; } = new(StringComparer.Ordinal);

    /// <summary>Status used when a <c>DomainException</c> code is not registered and has no explicit status.</summary>
    public int DefaultDomainExceptionStatusCode { get; set; } = StatusCodes.Status409Conflict;

    public GrottoWorksWebOptions RegisterErrorCode(string code, int statusCode, string? title = null)
    {
        ErrorCodes[code] = new ErrorCodeMapping(statusCode, title);
        return this;
    }
}

public sealed record ErrorCodeMapping(int StatusCode, string? Title);
