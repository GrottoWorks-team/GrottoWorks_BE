using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Infrastructure.Data;

/// <summary>
/// Translates PostgreSQL unique violations (SQLSTATE 23505) so that a check-then-insert race
/// surfaces as the documented 409 instead of a 500.
/// </summary>
internal static class DbUpdateExceptionExtensions
{
    public const string AppUserEmailIndex = "ux_app_user_email";
    public const string SkillCodeIndex = "ux_skill_skill_code";

    public static bool IsUniqueViolation(this DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        string.Equals(postgresException.ConstraintName, constraintName, StringComparison.Ordinal);
}
