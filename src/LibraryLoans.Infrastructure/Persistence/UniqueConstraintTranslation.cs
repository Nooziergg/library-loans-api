using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Turns a PostgreSQL unique-violation into the domain error it actually represents.
///
/// Matching is on the <i>constraint name</i>, never on the SQL state alone. Every unique index
/// in the database raises the same 23505, so a state-only match would report an unrelated
/// collision as a duplicate ISBN — a wrong answer that looks like a right one. An unrecognised
/// constraint returns null, and the caller rethrows: a violation nobody has mapped is a bug to
/// surface, not a 409 to invent.
/// </summary>
internal static class UniqueConstraintTranslation
{
    public static DomainError? Translate(string? constraintName) => constraintName switch
    {
        DatabaseConstraints.BooksIsbnUniqueIndex => BookErrors.DuplicateIsbn(),
        _ => null,
    };
}
