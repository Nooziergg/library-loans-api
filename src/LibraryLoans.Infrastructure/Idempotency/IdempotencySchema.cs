namespace LibraryLoans.Infrastructure.Idempotency;

/// <summary>
/// Table and column names for the idempotency table, in one place.
///
/// These are needed twice — once by the EF Core mapping, once inside the raw SQL the store issues —
/// and the compiler can check neither string against the other. A constant means a rename cannot
/// leave three statements pointing at a column that no longer exists, which is the same reason
/// <c>LoanConfiguration</c> holds its column names as constants for the index filter.
/// </summary>
internal static class IdempotencySchema
{
    public const string Table = "idempotency_keys";

    /// <summary>
    /// Not <c>key</c>. PostgreSQL permits it as an identifier, but it is a keyword in enough
    /// dialects and tools that a column called <c>key</c> eventually has to be quoted by someone.
    /// </summary>
    public const string KeyColumn = "idempotency_key";

    public const string FingerprintColumn = "fingerprint";

    public const string CreatedAtColumn = "created_at";

    public const string StatusCodeColumn = "status_code";

    public const string ContentTypeColumn = "content_type";

    public const string HeadersColumn = "headers";

    public const string BodyColumn = "body";
}
