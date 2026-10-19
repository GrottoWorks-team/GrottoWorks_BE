namespace ParishCoordination.Api.Common;

public enum SeasonVersionPreconditionError
{
    None,
    InvalidIfMatch,
    VersionMismatch
}

public sealed record SeasonVersionPreconditionResult(
    int? Version,
    SeasonVersionPreconditionError Error);

public static class SeasonVersionPrecondition
{
    public static SeasonVersionPreconditionResult Resolve(
        int? bodyVersion,
        string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return new SeasonVersionPreconditionResult(
                bodyVersion,
                SeasonVersionPreconditionError.None);
        }

        var value = ifMatch.Trim();
        var startsWithQuote = value.StartsWith('"');
        var endsWithQuote = value.EndsWith('"');

        if (startsWithQuote || endsWithQuote)
        {
            if (!startsWithQuote || !endsWithQuote || value.Length <= 2)
            {
                return InvalidIfMatch();
            }

            value = value[1..^1];
        }

        if (!int.TryParse(value, out var headerVersion) || headerVersion <= 0)
        {
            return InvalidIfMatch();
        }

        if (bodyVersion.HasValue && bodyVersion.Value != headerVersion)
        {
            return new SeasonVersionPreconditionResult(
                null,
                SeasonVersionPreconditionError.VersionMismatch);
        }

        return new SeasonVersionPreconditionResult(
            headerVersion,
            SeasonVersionPreconditionError.None);
    }

    private static SeasonVersionPreconditionResult InvalidIfMatch() =>
        new(null, SeasonVersionPreconditionError.InvalidIfMatch);
}
