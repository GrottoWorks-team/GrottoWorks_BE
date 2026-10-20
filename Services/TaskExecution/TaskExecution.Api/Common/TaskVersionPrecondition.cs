namespace TaskExecution.Api.Common;

public static class TaskVersionPrecondition
{
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
