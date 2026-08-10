namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Names of the database constraints this system relies on for correctness.
///
/// These are named explicitly rather than left to EF Core's naming convention because the
/// names are load-bearing: when PostgreSQL rejects a write, the constraint name is the only
/// thing in the error that says <i>which rule</i> was broken, and
/// <see cref="UniqueConstraintTranslation"/> matches on it to produce the right response. A
/// convention-generated name would work until a rename silently turned a 409 into a 500.
/// </summary>
internal static class DatabaseConstraints
{
    /// <summary>
    /// Enforces that one ISBN appears once in the catalogue. The application checks this
    /// before inserting; this index is what holds when two requests check at the same moment.
    /// </summary>
    public const string BooksIsbnUniqueIndex = "ix_books_isbn";

    /// <summary>
    /// Named only for consistency: EF Core's default would be <c>PK_books</c>, which is the one
    /// PascalCase identifier in an otherwise snake_case schema and therefore the one a reader
    /// has to quote in psql.
    /// </summary>
    public const string BooksPrimaryKey = "pk_books";
}
