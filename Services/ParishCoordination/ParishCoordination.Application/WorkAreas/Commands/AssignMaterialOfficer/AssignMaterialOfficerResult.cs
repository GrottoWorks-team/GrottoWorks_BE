namespace ParishCoordination.Application.WorkAreas.Commands.AssignMaterialOfficer;

public enum AssignMaterialOfficerOutcome
{
    Success,
    WorkAreaNotFound,
    SeasonNotFound,
    SeasonInvalidState,
    UserNotFound,
    UserNotActive,
    UserRoleMismatch,
    IdentityServiceUnavailable,
    VersionMismatch
}

public sealed record AssignMaterialOfficerResult(
    AssignMaterialOfficerOutcome Outcome,
    WorkAreaDto? WorkArea)
{
    public static AssignMaterialOfficerResult Success(WorkAreaDto workArea) =>
        new(AssignMaterialOfficerOutcome.Success, workArea);

    public static AssignMaterialOfficerResult WorkAreaNotFound() =>
        new(AssignMaterialOfficerOutcome.WorkAreaNotFound, null);

    public static AssignMaterialOfficerResult SeasonNotFound() =>
        new(AssignMaterialOfficerOutcome.SeasonNotFound, null);

    public static AssignMaterialOfficerResult SeasonInvalidState() =>
        new(AssignMaterialOfficerOutcome.SeasonInvalidState, null);

    public static AssignMaterialOfficerResult UserNotFound() =>
        new(AssignMaterialOfficerOutcome.UserNotFound, null);

    public static AssignMaterialOfficerResult UserNotActive() =>
        new(AssignMaterialOfficerOutcome.UserNotActive, null);

    public static AssignMaterialOfficerResult UserRoleMismatch() =>
        new(AssignMaterialOfficerOutcome.UserRoleMismatch, null);

    public static AssignMaterialOfficerResult IdentityServiceUnavailable() =>
        new(AssignMaterialOfficerOutcome.IdentityServiceUnavailable, null);

    public static AssignMaterialOfficerResult VersionMismatch() =>
        new(AssignMaterialOfficerOutcome.VersionMismatch, null);
}
