using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Commits the current unit of work, translating the database's uniqueness rulings into domain
/// errors.
///
/// This is the second half of enforcing a uniqueness rule properly, and the half most
/// submissions leave out. A handler that checks "does this already exist?" and then inserts has
/// a gap between the two statements; under concurrency both requests can read "no" and both can
/// try to insert. The unique index closes that gap by deciding the winner, and this class is
/// what turns the loser's exception into an ordinary 409 instead of a 500.
///
/// It takes the same scoped <see cref="LibraryDbContext"/> the repositories take, which is what
/// makes "stage the entity, then save" work at all: two contexts would mean the entity is
/// tracked by one and the save runs on the other, and the write would silently vanish.
///
/// One thing to know before adding a handler that saves twice: after a translated failure the
/// rejected entity is still tracked in the Added state. That is harmless today, because the
/// context is scoped per request and disposed straight after, and no handler saves more than
/// once. It stops being harmless the moment one does — the second save re-attempts the entity
/// that already failed, and the resulting error has no obvious connection to the code that
/// triggered it. Detach it here at the point that changes.
/// </summary>
internal sealed class UnitOfWork(LibraryDbContext dbContext, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    public async Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Result<int>.Success(await dbContext.SaveChangesAsync(cancellationToken));
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation,
                  } postgresException)
        {
            var error = UniqueConstraintTranslation.Translate(postgresException.ConstraintName);

            if (error is null)
            {
                // A uniqueness rule exists in the schema that nothing here knows how to
                // describe. Rethrowing surfaces it as a 500 and a logged fault, which is
                // correct: guessing at a friendly message would hide a real modelling gap.
                throw;
            }

            logger.LogInformation(
                "Write rejected by unique constraint {ConstraintName}, reported as {ErrorCode}",
                postgresException.ConstraintName,
                error.Code);

            return Result<int>.Failure(error);
        }
    }
}
