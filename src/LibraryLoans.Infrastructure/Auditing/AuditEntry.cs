namespace LibraryLoans.Infrastructure.Auditing;

/// <summary>
/// One row of the audit trail: a record that some entity changed, who changed it, when, under which
/// request, and what the change was.
///
/// <para><b>Why this lives in Infrastructure and not in the Domain.</b> It is tempting to call this
/// an aggregate, it has an id and a lifecycle, but it is not part of the library's language. No
/// librarian reasons about audit entries, no invariant mentions them, and nothing in the Domain
/// would compile differently if the trail did not exist. What it actually records is *persistence*:
/// rows changing in tables. Putting it in the Domain would mean the Domain knew it was stored, which
/// is the exact coupling the dependency rule exists to prevent.</para>
///
/// <para><b>Internal, and mapped without a <c>DbSet</c>.</b> Both are deliberate. The type is not
/// visible outside this assembly and the context advertises no property for it, so there is no
/// surface through which application code could write an audit row by hand. The interceptor is the
/// only writer, and that is enforced by the type system rather than by a convention someone has to
/// know about. Reading it back is a query, from psql, from a reporting tool, or from the tests,
/// which go at it in SQL for the same reason.</para>
/// </summary>
internal sealed class AuditEntry
{
    public AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        string entityType,
        string entityId,
        AuditAction action,
        string actor,
        string? correlationId,
        string? changes)
    {
        Id = id;
        OccurredAt = occurredAt;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Actor = actor;
        CorrelationId = correlationId;
        Changes = changes;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>The aggregate's CLR name (<c>Loan</c>, <c>Book</c>), not the table name.</summary>
    public string EntityType { get; }

    /// <summary>
    /// The primary key, as text. Text rather than <c>uuid</c> because a key is not guaranteed to be
    /// one column or one type forever, and an audit table that has to be migrated when an unrelated
    /// entity changes its key is a liability. The cost is that this column is filtered by equality
    /// only, which is all anyone asks of it.
    /// </summary>
    public string EntityId { get; }

    public AuditAction Action { get; }

    /// <summary>See <c>IAuditContext.Actor</c>: today this is <c>anonymous</c> or <c>system</c>.</summary>
    public string Actor { get; }

    public string? CorrelationId { get; }

    /// <summary>
    /// The change itself, as JSON, and what goes in here differs by action on purpose: the rule is
    /// <i>record what would otherwise be unrecoverable</i>.
    ///
    /// <list type="bullet">
    /// <item><b>Created</b>: null. The row is right there in its own table; copying it here would
    /// double the storage to say the same thing twice.</item>
    /// <item><b>Updated</b>: the delta only, as <c>{"Title":{"old":...,"new":...}}</c>. The current
    /// value is in the table; what the table cannot tell you is what it used to be.</item>
    /// <item><b>Deleted</b>: every value the row had. This is the one case where the data is
    /// genuinely gone, so it is the one case worth copying in full.</item>
    /// </list>
    ///
    /// <para>A note on personal data, because it is a real consideration rather than a hypothetical:
    /// a member's name and email can appear here on update and delete. That is a deliberate
    /// difference from the logging rule, which forbids personal data outright: a log stream is
    /// shipped off the machine, fanned out to aggregators and read by anyone on call, while this is a
    /// table in the same database as the members table, under the same access control, and being able
    /// to answer "who changed this member's email and what was it before" is most of the reason an
    /// audit trail exists at all. What a production system would add on top is field-level redaction
    /// for anything genuinely secret and a retention policy that expires rows on a schedule; both are
    /// noted in the README rather than implied to be here.</para>
    /// </summary>
    public string? Changes { get; }
}
