using BuildingBlocks.Web.Exceptions;

namespace TaskExecution.Api.Common;

public static class QueryEnum
{
    public static T? Parse<T>(string? value, string field) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value.Replace("_", ""), true, out var result) && Enum.IsDefined(result)) return result;
        throw new DomainException("INVALID_FILTER", $"Invalid {field}.", StatusCodes.Status400BadRequest);
    }
}
