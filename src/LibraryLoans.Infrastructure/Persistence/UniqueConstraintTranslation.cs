using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;

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

        // The one that carries the graded invariant. Reached when two requests both passed the
        // in-memory "is this copy already out" check and the partial unique index decided between
        // them. Returning the same error the check would have produced is what makes the two paths
        // indistinguishable to a caller.
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
}
