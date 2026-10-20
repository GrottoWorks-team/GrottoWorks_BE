namespace TaskExecution.Domain;

public enum DomainErrorKind
{
    Validation,
    Conflict,
    NotFound,
    Forbidden,
    BadRequest,
    PreconditionFailed
}

public sealed class DomainRuleException(
    string code,
    string message,
    DomainErrorKind kind = DomainErrorKind.Conflict) : Exception(message)
{
    public string Code { get; } = code;

    public DomainErrorKind Kind { get; } = kind;

    public static DomainRuleException Validation(string code, string message) =>
        new(code, message, DomainErrorKind.Validation);

    public static DomainRuleException NotFound(string code, string message) =>
        new(code, message, DomainErrorKind.NotFound);

    public static DomainRuleException Forbidden(string code, string message) =>
        new(code, message, DomainErrorKind.Forbidden);
}
