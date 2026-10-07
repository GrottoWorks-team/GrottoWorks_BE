namespace Identity.Domain;

/// <summary>
/// Violation of a business rule owned by the Identity bounded context.
/// <paramref name="Code"/> is the stable error code returned to clients
/// (for example <c>AUTH_INVALID_CREDENTIALS</c>); the API layer maps it to an HTTP status.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
