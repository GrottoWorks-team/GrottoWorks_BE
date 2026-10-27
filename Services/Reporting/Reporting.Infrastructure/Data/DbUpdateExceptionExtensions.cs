using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Reporting.Infrastructure.Data;

/// <summary>Translates PostgreSQL unique violations (23505) so check-then-insert races surface as 409.</summary>
internal static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        string.Equals(postgresException.ConstraintName, constraintName, StringComparison.Ordinal);
}
