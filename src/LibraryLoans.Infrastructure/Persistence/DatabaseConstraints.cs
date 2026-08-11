namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Names of the database constraints this system relies on for correctness.
///
/// These are named explicitly rather than left to EF Core's naming convention because the names are
/// load-bearing: when PostgreSQL rejects a write, the constraint name is the only thing in the error
/// that says <i>which rule</i> was broken, and <see cref="DatabaseConstraintTranslation"/> matches on
/// it to produce the right response. A convention-generated name would work until a rename silently
/// turned a 409 into a 500.
///
/// This class holds constraint and index names only. Column names live on the configuration that
/// owns them: a column name is a different kind of string, and mixing the two dilutes a class whose
/// entire value is that everything in it belongs to one category.
/// </summary>
internal static class DatabaseConstraints
{
    // -- Unique constraints whose violation is translated into a domain error ------------------
    // Every name here must have an arm in DatabaseConstraintTranslation. An unmapped one is a 500
    // where a 409 belongs.

    /// <summary>
    /// Enforces that one ISBN appears once in the catalogue. The application checks this before
    /// inserting; this index is what holds when two requests check at the same moment.
    /// </summary>
    public const string BooksIsbnUniqueIndex = "ix_books_isbn";

    /// <summary>
    /// **The centrepiece.** A copy may have at most one loan with no return date.
    ///
    /// This is a <b>partial</b> unique index, <c>WHERE returned_at IS NULL</c>, and the filter is
    /// the whole point. A plain unique index on <c>book_copy_id</c> would mean a copy could be
    /// borrowed once in its entire lifetime and never again, which is a different and much worse
    /// rule that happens to pass most of the same tests.
    /// </summary>
    public const string LoansActiveCopyUniqueIndex = "ix_loans_active_copy";

    public const string BookCopiesBarcodeUniqueIndex = "ix_book_copies_barcode";

    public const string MembersMembershipNumberUniqueIndex = "ix_members_membership_number";

    // -- Supporting indexes -------------------------------------------------------------------

    /// <summary>
    /// Non-unique, partial. Backs the active-loan count that runs on every borrow; without it that
    /// count is a sequential scan over the whole loan history, so the write path on the busiest
    /// endpoint would get slower for as long as the library keeps lending.
    /// </summary>
    public const string LoansMemberActiveIndex = "ix_loans_member_active";

    /// <summary>
    /// Declared rather than left to EF Core, which would create it implicitly for the foreign key
    /// and name it <c>IX_book_copies_book_id</c>: the one PascalCase identifier in a snake_case
    /// schema. It also stops being incidental: "which copies does this title have" is a question the
    /// catalogue will ask directly.
    /// </summary>
    public const string BookCopiesBookIndex = "ix_book_copies_book_id";

    /// <summary>
    /// Trigram indexes backing catalogue search.
    ///
    /// A substring match compiles to <c>ILIKE '%term%'</c>, and a leading wildcard cannot use a
    /// B-tree, so without these, "search" would be a sequential scan and calling it scalable would
    /// be a claim the schema does not support. GIN with <c>gin_trgm_ops</c> is the index that makes
    /// the shape correct; at this catalogue's size PostgreSQL will still choose a scan, and the
    /// README says so rather than implying otherwise.
    /// </summary>
    public const string BooksTitleTrigramIndex = "ix_books_title_trgm";

    public const string BooksAuthorTrigramIndex = "ix_books_author_trgm";

    /// <summary>
    /// "Everything that ever happened to this entity": the question the audit trail exists to
    /// answer, over a table that only ever grows.
    /// </summary>
    public const string AuditEntriesEntityIndex = "ix_audit_entries_entity";

    /// <summary>
    /// Time-ordered access to the audit trail. Also what a retention job would use to find the rows
    /// older than its cutoff, which is a scan of the whole table without it.
    /// </summary>
    public const string AuditEntriesOccurredAtIndex = "ix_audit_entries_occurred_at";

    /// <summary>
    /// What a retention job would delete idempotency keys on. Keys are worth keeping only as long as
    /// a client might still retry, and without expiry the table grows for the lifetime of the
    /// service.
    /// </summary>
    public const string IdempotencyKeysCreatedAtIndex = "ix_idempotency_keys_created_at";

    // -- Check constraints --------------------------------------------------------------------

    /// <summary>
    /// Deliberately <b>not</b> translated. It raises SQLSTATE 23514, not 23505, so
    /// <see cref="DatabaseConstraintTranslation"/> never sees it and the unit of work lets it through
    /// as a fault. That is correct: the domain computes the due date from the loan date, so this
    /// constraint is unreachable through any code path. If it ever fires, something wrote to the
    /// database directly and a 500 with a logged stack trace is exactly the right alarm.
    /// </summary>
    public const string LoansDueAfterLoanedCheck = "ck_loans_due_after_loaned";

    // -- Primary and foreign keys, named for schema consistency -------------------------------

    /// <summary>
    /// Named only for consistency: EF Core's default would be <c>PK_books</c>, which is the one
    /// PascalCase identifier in an otherwise snake_case schema and therefore the one a reader has to
    /// quote in psql.
    /// </summary>
    public const string BooksPrimaryKey = "pk_books";

    public const string MembersPrimaryKey = "pk_members";

    public const string BookCopiesPrimaryKey = "pk_book_copies";

    public const string LoansPrimaryKey = "pk_loans";

    public const string AuditEntriesPrimaryKey = "pk_audit_entries";

    /// <summary>
    /// Not merely a primary key: this one is the idempotency mechanism. Two concurrent requests
    /// carrying the same <c>Idempotency-Key</c> both insert this row, and PostgreSQL decides which
    /// of them owns it. Same technique as <see cref="LoansActiveCopyUniqueIndex"/>: the gap between
    /// checking and inserting is where duplicates are born, and only the database can close it.
    /// </summary>
    public const string IdempotencyKeysPrimaryKey = "pk_idempotency_keys";

    public const string BookCopiesBookForeignKey = "fk_book_copies_books";

    public const string LoansBookCopyForeignKey = "fk_loans_book_copies";

    public const string LoansMemberForeignKey = "fk_loans_members";
}
