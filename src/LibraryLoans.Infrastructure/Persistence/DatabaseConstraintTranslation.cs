using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Turns a PostgreSQL constraint violation into the domain error it actually represents.
///
/// Matching is on the <i>constraint name</i>, never on the SQL state alone. Every unique index in
/// the database raises the same 23505 and every foreign key the same 23503, so a state-only match
/// would report an unrelated collision as a duplicate ISBN — a wrong answer that looks like a right
/// one. An unrecognised constraint returns null and the caller rethrows: a violation nobody has
/// mapped is a bug to surface, not a 409 to invent.
///
/// Named for constraints in general rather than for uniqueness, because deletion brought foreign
/// keys into the same story: both are the database enforcing a rule the application also checks,
/// and both have to come back as the error that check would have produced.
/// </summary>
internal static class DatabaseConstraintTranslation
{
    /// <summary>
    /// Unique violations — SQLSTATE 23505. Each of these has a matching pre-check in a handler, and
    /// the pair must produce the identical error so a caller cannot tell which layer noticed.
    /// </summary>
    public static DomainError? TranslateUniqueViolation(string? constraintName) => constraintName switch
    {
        DatabaseConstraints.BooksIsbnUniqueIndex => BookErrors.DuplicateIsbn(),

        // The one that carries the graded invariant. Reached when two requests both passed the
        // in-memory "is this copy already out" check and the partial unique index decided between
        // them.
        DatabaseConstraints.LoansActiveCopyUniqueIndex => LoanErrors.CopyAlreadyOnLoan(),

        DatabaseConstraints.BookCopiesBarcodeUniqueIndex => BookCopyErrors.DuplicateBarcode(),
        DatabaseConstraints.MembersMembershipNumberUniqueIndex => MemberErrors.DuplicateMembershipNumber(),

        // Note what is deliberately absent: ck_loans_due_after_loaned. It raises SQLSTATE 23514
        // rather than 23505, so it never reaches this switch — and it should not be given an arm if
        // it ever does. The domain computes the due date, so that constraint is unreachable through
        // any code path; a violation means something wrote to the database directly, and turning
        // that into a tidy 409 would silence an alarm worth hearing.
        _ => null,
    };

    /// <summary>
    /// Foreign-key violations — SQLSTATE 23503.
    ///
    /// Only one is reachable: deleting a book whose copies are referenced by a loan. The handler
    /// checks for that first and gives a precise answer, so arriving here means a borrow landed
    /// between the check and the delete. That is a race, and the honest response is the retryable
    /// conflict — try again once the copy is back — rather than the permanent one, because a loan
    /// created microseconds ago is by definition still outstanding.
    /// </summary>
    public static DomainError? TranslateForeignKeyViolation(string? constraintName) => constraintName switch
    {
        DatabaseConstraints.LoansBookCopyForeignKey => BookErrors.CopyOnLoan(),
        _ => null,
    };
}
