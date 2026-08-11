namespace LibraryLoans.Infrastructure.Auditing;

/// <summary>
/// What happened to a row. Deliberately three values rather than a free-text verb: an audit trail
/// is only queryable if the thing you filter on comes from a closed set.
///
/// Stored as text rather than as its integer value, because the first thing anyone does with an
/// audit table is read it in psql, and <c>2</c> means nothing there. The cost is a few bytes a row
/// against a column that is written once and read by humans.
/// </summary>
internal enum AuditAction
{
    Created,
    Updated,
    Deleted,
}
