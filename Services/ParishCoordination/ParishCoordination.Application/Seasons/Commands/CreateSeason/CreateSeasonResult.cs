namespace ParishCoordination.Application.Seasons.Commands.CreateSeason;

public enum CreateSeasonOutcome
{
    Success,
    ParishNotFound,
    NameConflict,
    YearConflict
}

public sealed record CreateSeasonResult(
    CreateSeasonOutcome Outcome,
    SeasonDto? Season)
{
    public static CreateSeasonResult Success(SeasonDto season) =>
        new(CreateSeasonOutcome.Success, season);

    public static CreateSeasonResult ParishNotFound() =>
        new(CreateSeasonOutcome.ParishNotFound, null);

    public static CreateSeasonResult NameConflict() =>
        new(CreateSeasonOutcome.NameConflict, null);

    public static CreateSeasonResult YearConflict() =>
        new(CreateSeasonOutcome.YearConflict, null);
}