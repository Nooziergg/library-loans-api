using LibraryLoans.Application.Abstractions;
using LibraryLoans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LibraryLoans.Infrastructure.Idempotency;

/// <summary>
/// Idempotency keys in the same PostgreSQL the rest of the service uses.
///
/// <para><b>Every write here is raw SQL, on purpose, and it is the most important thing in this
/// file.</b> The obvious implementation adds an entity and calls <c>SaveChanges</c> — but this store
/// shares the request's scoped <c>DbContext</c>, and <c>SaveChanges</c> flushes <i>everything</i>
/// pending on it. The middleware that calls this runs before and after the endpoint, so a flush at
/// either point could commit a handler's half-built unit of work, or commit it a second time. Going
/// straight to SQL means these three statements touch exactly the rows they name and the change
/// tracker is never consulted. A useful side effect: the audit interceptor never sees them either,
/// which is right — an idempotency key is transport plumbing, not a fact about the library.</para>
///
/// <para><b>Deliberately not in the caller's transaction.</b> The claim has to be visible to a
/// concurrent duplicate <i>while</i> the original is still working, so it cannot wait for the
/// business transaction to commit. The cost is stated rather than hidden: if the process dies
/// between the business commit and <see cref="CompleteAsync"/>, the key is left claimed with no
/// response, and a retry is told the request is still in progress. That is the safe direction to
/// fail — the alternative is a retry re-running a change that already committed — but it does mean a
/// stuck key needs the retention job to clear it.</para>
/// </summary>
internal sealed class EfIdempotencyStore(LibraryDbContext dbContext, TimeProvider timeProvider) : IIdempotencyStore
{
    /// <summary>
    /// <c>ON CONFLICT DO NOTHING</c> rather than catching a unique violation. Same arbitration by the
    /// same index, but it reports the loss as "zero rows affected" instead of as an exception — so
    /// the ordinary, expected case of a duplicate retry costs no exception and, more importantly,
    /// does not poison the ambient transaction the way a raised error would.
    /// </summary>
    private static readonly string ClaimSql =
        $"""
         INSERT INTO {IdempotencySchema.Table}
             ({IdempotencySchema.KeyColumn}, {IdempotencySchema.FingerprintColumn}, {IdempotencySchema.CreatedAtColumn})
         VALUES (@key, @fingerprint, @createdAt)
         ON CONFLICT ({IdempotencySchema.KeyColumn}) DO NOTHING
         """;

    private static readonly string CompleteSql =
        $"""
         UPDATE {IdempotencySchema.Table}
         SET {IdempotencySchema.StatusCodeColumn} = @statusCode,
             {IdempotencySchema.ContentTypeColumn} = @contentType,
             {IdempotencySchema.BodyColumn} = @body
         WHERE {IdempotencySchema.KeyColumn} = @key
         """;

    private static readonly string ReleaseSql =
        $"""
         DELETE FROM {IdempotencySchema.Table}
         WHERE {IdempotencySchema.KeyColumn} = @key
         """;

    public async Task<IdempotencyReservation> ReserveAsync(
        string key,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var claimed = await dbContext.Database.ExecuteSqlRawAsync(
            ClaimSql,
            [
                new NpgsqlParameter("key", key),
                new NpgsqlParameter("fingerprint", fingerprint),
                new NpgsqlParameter("createdAt", timeProvider.GetUtcNow()),
            ],
            cancellationToken);

        if (claimed == 1)
        {
            return new IdempotencyReservation(IdempotencyOutcome.Reserved, null);
        }

        // Somebody else holds it. A read, not a guess, because what to do next depends on whether
        // they finished and on whether they were even making the same request.
        var existing = await dbContext.Set<IdempotencyKey>()
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Key == key, cancellationToken);

        if (existing is null)
        {
            // The row was released between the insert and this read — the original request failed
            // with a server fault in that window. Reporting "in progress" asks the client to retry,
            // which is correct and, at one lost round trip, cheaper than the alternatives: claiming
            // it here would need a second insert that can lose the same race again.
            return new IdempotencyReservation(IdempotencyOutcome.InProgress, null);
        }

        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyReservation(IdempotencyOutcome.FingerprintMismatch, null);
        }

        if (existing.StatusCode is not { } statusCode)
        {
            return new IdempotencyReservation(IdempotencyOutcome.InProgress, null);
        }

        return new IdempotencyReservation(
            IdempotencyOutcome.Completed,
            new IdempotentResponse(statusCode, existing.ContentType, existing.Body ?? []));
    }

    public Task CompleteAsync(string key, IdempotentResponse response, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            CompleteSql,
            [
                new NpgsqlParameter("key", key),
                new NpgsqlParameter("statusCode", response.StatusCode),
                new NpgsqlParameter("contentType", (object?)response.ContentType ?? DBNull.Value),
                new NpgsqlParameter("body", response.Body),
            ],
            cancellationToken);

    public Task ReleaseAsync(string key, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            ReleaseSql,
            [new NpgsqlParameter("key", key)],
            cancellationToken);
}
