namespace TaskExecution.Api.Common;

public static class TaskVersionPrecondition
{
    public static int? Resolve(int? bodyVersion, string? ifMatch)
    {
        if (!TryParse(ifMatch, out var headerVersion))
        {
            throw new BuildingBlocks.Web.Exceptions.DomainException(
                "INVALID_IF_MATCH", "If-Match must contain a positive integer version.", 400);
        }

        if (bodyVersion is <= 0)
        {
            throw TaskExecution.Domain.DomainRuleException.Validation("VALIDATION_FAILED", "Version must be positive.");
        }

        if (bodyVersion.HasValue && headerVersion.HasValue && bodyVersion != headerVersion)
        {
            throw new TaskExecution.Domain.DomainRuleException("TASK_VERSION_MISMATCH",
                "Body version and If-Match disagree.", TaskExecution.Domain.DomainErrorKind.PreconditionFailed);
        }

        return headerVersion ?? bodyVersion;
    }

    public static bool TryParse(string? ifMatch, out int? expectedVersion)
    {
        expectedVersion = null;
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return true;
        }

        var value = ifMatch.Trim();
        var startsWithQuote = value.StartsWith('"');
        var endsWithQuote = value.EndsWith('"');
        if (startsWithQuote || endsWithQuote)
        {
            if (!startsWithQuote || !endsWithQuote || value.Length <= 2)
            {
                return false;
            }

            value = value[1..^1];
        }

        if (!int.TryParse(value, out var version) || version <= 0)
        {
            return false;
        }

        expectedVersion = version;
        return true;
    }
}
