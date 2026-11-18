using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Trimme.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Database outcomes that use cases translate into typed answers (so Application code needs no Npgsql).</summary>
public static class DatabaseErrors
{
    /// <summary>An exclusion constraint refused the row (for example two overlapping bookings, R-BKG-03).</summary>
    public static bool IsExclusionViolation(DbUpdateException exception) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

    /// <summary>
    /// Two writers raced for the same time: an exclusion constraint refused the row, or PostgreSQL broke a deadlock
    /// between them (concurrent inserts checked by one GiST exclusion constraint can wait on each other). Either way the
    /// victim lost to a conflicting write (R-BKG-04). EF wraps transient errors, so inner exceptions are searched.
    /// </summary>
    public static bool IsLostRace(Exception exception) =>
        Find(exception) is { SqlState: PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.DeadlockDetected };

    /// <summary>A unique index or primary key refused the row (for example an idempotency key already claimed).</summary>
    public static bool IsUniqueViolation(Exception exception) =>
        Find(exception) is { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// A unique index whose name contains <paramref name="column"/> refused the row: a value another row already has, which
    /// the caller's own query may not have seen (a tenant-filtered check sees one shop; the index sees them all, D-146).
    /// </summary>
    public static bool IsUniqueViolationOn(Exception exception, string column) =>
        Find(exception) is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && name.Contains(column, StringComparison.Ordinal);

    private static PostgresException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }
}
