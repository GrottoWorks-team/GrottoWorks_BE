namespace Resource.Domain;

/// <summary>How a rule violation maps to HTTP (API Contract 4.3). The Api layer does the mapping.</summary>
public enum DomainErrorKind
{
    /// <summary>422 — semantically invalid field values.</summary>
    Validation,

    /// <summary>409 — business rule, state or uniqueness conflict.</summary>
    Conflict,

    /// <summary>404 — the resource does not exist or is hidden by scope.</summary>
    NotFound,

    /// <summary>403 — authenticated but outside role or data scope.</summary>
    Forbidden,

    /// <summary>400 — malformed request or parameter syntax.</summary>
    BadRequest,

    /// <summary>412 — optimistic concurrency precondition failed.</summary>
    PreconditionFailed,

    /// <summary>503 — a service this operation depends on (for example Identity) is unavailable.</summary>
    ServiceUnavailable
}

/// <summary>
/// Violation of a rule owned by the Resource bounded context, with a stable client-facing <see cref="Code"/>
/// (for example <c>MATERIAL_CODE_ALREADY_EXISTS</c>). The Domain stays free of ASP.NET; the Api translates
/// this into BuildingBlocks ProblemDetails.
/// </summary>
public sealed class DomainRuleException(string code, string message, DomainErrorKind kind = DomainErrorKind.Conflict)
    : Exception(message)
{
    public string Code { get; } = code;

    public DomainErrorKind Kind { get; } = kind;

    public static DomainRuleException Validation(string code, string message) => new(code, message, DomainErrorKind.Validation);

    public static DomainRuleException NotFound(string code, string message) => new(code, message, DomainErrorKind.NotFound);

    public static DomainRuleException Forbidden(string code, string message) => new(code, message, DomainErrorKind.Forbidden);
}
