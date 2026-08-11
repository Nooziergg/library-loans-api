namespace LibraryLoans.Application.Abstractions;

/// <summary>
/// A response that was already produced for an idempotency key, kept so that a retry of the same
/// request can be answered with it instead of doing the work twice.
/// </summary>
/// <param name="Headers">
/// The response headers worth reproducing, which is deliberately not all of them.
///
/// <para><b>A replay that drops <c>Location</c> is not a replay.</b> Every creating endpoint here
/// answers with <c>201</c> and a <c>Location</c>, and a client that follows it — a generated SDK,
/// most obviously — gets null on the retry path, which is the exact path this whole mechanism exists
/// to serve. Storing the status and the body alone is the version of this feature that passes its
/// own tests and fails its first real client.</para>
///
/// <para>An allowlist rather than everything, because most of a response's headers describe <i>this</i>
/// exchange and not the outcome: <c>Date</c>, <c>Server</c>, connection handling and any
/// <c>Set-Cookie</c> belong to the call that is happening now, and <c>Content-Length</c> is
/// recalculated from the body being written.</para>
/// </param>
public sealed record IdempotentResponse(
    int StatusCode,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

/// <summary>What claiming an idempotency key turned out to mean.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Nobody had used this key. The caller owns it and should do the work.</summary>
    Reserved,

    /// <summary>
    /// The key was claimed by a request that has not finished. Two copies of the same request are in
    /// flight — the second must not proceed, and must not be told it succeeded either.
    /// </summary>
    InProgress,

    /// <summary>The key has a stored response. Replay it verbatim.</summary>
    Completed,

    /// <summary>
    /// The key exists, but against a different request. Almost always a client bug — a key reused
    /// across two genuinely different calls — and answering it by replaying the first response would
    /// silently discard the second.
    /// </summary>
    FingerprintMismatch,
}

public sealed record IdempotencyReservation(IdempotencyOutcome Outcome, IdempotentResponse? Response);

/// <summary>
/// Where idempotency keys and their responses live.
///
/// <para>A port rather than a concrete class for the usual reason and one specific one: this is the
/// piece most likely to move. A single-instance service can hold these rows in its own database, as
/// this implementation does; a fleet behind a load balancer usually wants them in something shared
/// and expiring, such as Redis. That is a different implementation of these three methods and
/// nothing else — in particular, not a change to the middleware that calls them.</para>
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims the key for this request, atomically. The implementation must make concurrent claims of
    /// the same key resolve to exactly one <see cref="IdempotencyOutcome.Reserved"/> — this is the
    /// whole mechanism, and a check-then-insert would defeat it.
    /// </summary>
    Task<IdempotencyReservation> ReserveAsync(string key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>Stores the response, so that later retries of this key replay it.</summary>
    Task CompleteAsync(string key, IdempotentResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the key back, so an identical request may be attempted again. Used when the outcome was
    /// a server fault: replaying a 500 forever would turn a transient failure into a permanent one
    /// for any client that retries with the same key, which is precisely the well-behaved client.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
