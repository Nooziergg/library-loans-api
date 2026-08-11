using LibraryLoans.Domain.Loans;

namespace LibraryLoans.Application.Loans;

/// <summary>
/// The public shape of a loan. Positional, for the reason given on <c>BookResponse</c>: two places
/// construct it and a positional record makes adding a field a compile error in both.
///
/// Deliberately carries no <c>IsOverdue</c>. Overdue is <c>ReturnedAt is null &amp;&amp; DueAt &lt;
/// now</c>: a rule, and one that a database cannot evaluate from a domain method. Including the
/// flag would force either materialising the entity and mapping it in memory, or re-expressing the
/// rule inline in the SQL projection so it exists twice in two languages. It arrives with the
/// filtered loan list, where the rule has to be written once in a form both C# and SQL can use.
/// Clients that need it today can compare <c>DueAt</c> to the current time, which is the same
/// comparison with no risk of the two answers drifting.
/// </summary>
public sealed record LoanResponse(
    Guid Id,
    Guid BookCopyId,
    Guid MemberId,
    DateTimeOffset LoanedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? ReturnedAt);

public static class LoanMappings
{
    public static LoanResponse ToResponse(this Loan loan) =>
        new(loan.Id, loan.BookCopyId, loan.MemberId, loan.LoanedAt, loan.DueAt, loan.ReturnedAt);
}

public sealed record BorrowCopyCommand(Guid MemberId, Guid BookCopyId);
