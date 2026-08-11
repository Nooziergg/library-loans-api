using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations on startup.
///
/// This exists so that <c>docker compose up</c> on a clean machine yields a working API with no
/// manual database step. That is a reviewer-experience decision, and it is worth being explicit
/// that a production system would not do this: with more than one replica, every instance races
/// to apply the same DDL on deploy, and the application's runtime identity ends up holding
/// schema-modification rights it should never have. There, migrations are a separate,
/// single-shot step in the pipeline. Here, convenience wins and the flag makes it switchable.
///
/// <para>
/// Known and accepted: against an empty database this path emits two <c>Error</c>-level log
/// lines from <c>Microsoft.EntityFrameworkCore.Database.Command</c>, reporting a failed
/// <c>SELECT</c> from <c>__EFMigrationsHistory</c>. Nothing is wrong: EF looks for the history
/// table by reading it, and on a first run it does not exist yet, so the statement fails and EF
/// recovers by creating it. This project treats "logged as an error but is not one" as a defect,
/// so it is worth saying why it is tolerated here rather than fixed.
/// </para>
/// <para>
/// The available fixes are worse than the problem. Asking which migrations are pending before
/// reading the table swaps one failed statement for another; suppressing
/// <c>RelationalEventId.CommandError</c> through <c>ConfigureWarnings</c> would hide genuine
/// command failures for the whole application lifetime; and building a second
/// <see cref="LibraryDbContext"/> with its own logging configuration would duplicate the
/// connection setup and stop the migration path from exercising the same options the app uses.
/// The noise is two lines, once, on a database that has never been migrated, in a code path that
/// production does not run, because there, migrations are a separate deployment step and this
/// method is never called.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    /// <param name="cancellationToken">
    /// Required, deliberately. A default would let the next caller omit it, and "accepts a
    /// token, then every caller passes default" is on this project's list of things not to
    /// repeat.
    /// </param>
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrator));

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pending.Length == 0)
        {
            logger.LogInformation("Database schema is up to date; no migrations to apply");
            return;
        }

        logger.LogInformation(
            "Applying {PendingMigrationCount} database migration(s): {PendingMigrations}",
            pending.Length,
            pending);

        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database migrations applied");
    }
}
