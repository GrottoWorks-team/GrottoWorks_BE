namespace BuildingBlocks.Web.Exceptions;

/// <summary>
/// Business rule violation carrying a stable machine-readable <see cref="Code"/> (API Contract 4.3).
/// <see cref="StatusCode"/> optionally forces the HTTP status; when null the status comes from
/// <see cref="GrottoWorksWebOptions"/> (registered codes, then the default conflict status).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message, int? statusCode = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int? StatusCode { get; }
}
