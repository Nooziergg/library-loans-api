namespace LibraryLoans.Infrastructure.Idempotency;

/// <summary>
/// One claimed idempotency key, and the response it eventually produced.
///
/// <para>The primary key <i>is</i> the mechanism. Two concurrent requests carrying the same
/// <c>Idempotency-Key</c> both try to insert this row; PostgreSQL lets exactly one of them win, and
/// the loser learns from that rejection that it is a duplicate. This is the same technique as the
/// partial unique index on active loans — the database arbitrates a race the application cannot,
/// because between "does this key exist" and "insert it" there is a gap, and the gap is where the
/// duplicate charge, the duplicate loan and the duplicate order live.</para>
///
/// <para>A row exists in one of two states, distinguished by <see cref="StatusCode"/>: claimed but
/// unfinished (null), or complete (set). There is no explicit state column, because a second one
/// could disagree with the first.</para>
/// </summary>
internal sealed class IdempotencyKey
{
    public IdempotencyKey(
        string key,
        string fingerprint,
        DateTimeOffset createdAt,
        int? statusCode,
        string? contentType,
        string? headers,
        byte[]? body)
    {
        Key = key;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;
        StatusCode = statusCode;
        ContentType = contentType;
        Headers = headers;
        Body = body;
    }

    public string Key { get; }

    /// <summary>
    /// A hash of the request this key was claimed for — method, path and body. Kept so that reusing
    /// one key for two different requests is detected rather than served the wrong answer.
    ///
    /// A hash rather than the request itself: it is a fixed size whatever the payload, and it means
    /// the table does not become a second copy of every request body the service has ever received.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// When the key was claimed. Not decoration: it is what a retention job deletes on, and without
    /// expiry this table grows forever. See the README for why that job is described and not built.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    public int? StatusCode { get; }

    public string? ContentType { get; }

    /// <summary>
    /// The allowlisted response headers, as a JSON object — <c>Location</c> above all, without which
    /// a replayed <c>201</c> tells the client something was created and refuses to say where.
    /// </summary>
    public string? Headers { get; }

    public byte[]? Body { get; }
}
