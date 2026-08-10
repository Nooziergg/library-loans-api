using System.Linq.Expressions;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Members;

namespace LibraryLoans.Domain.Loans;

/// <summary>
/// A copy in a member's hands, from the moment it is borrowed until it comes back.
///
/// This is the aggregate the whole system is arranged around, because it carries the rule that a
/// physical object cannot be in two places at once.
/// </summary>
public sealed class Loan
{
    /// <summary>Materialization path for the ORM only.</summary>
    private Loan()
    {
    }

    private Loan(Guid id, Guid bookCopyId, Guid memberId, DateTimeOffset loanedAt, DateTimeOffset dueAt)
    {
        Id = id;
        BookCopyId = bookCopyId;
        MemberId = memberId;
        LoanedAt = loanedAt;
        DueAt = dueAt;
        ReturnedAt = null;
    }

    public Guid Id { get; private set; }

    // Ids, not navigation properties. A loan references two other aggregates, and holding
    // references to them would invite loading a graph on every read and blur which aggregate owns
    // which rule. The relationships are configured without navigations for the same reason.
    public Guid BookCopyId { get; private set; }

    public Guid MemberId { get; private set; }

    public DateTimeOffset LoanedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    /// <summary>Null while the copy is still out. This is what "active" means, everywhere.</summary>
    public DateTimeOffset? ReturnedAt { get; private set; }

    /// <summary>
    /// Derived, never stored, and explicitly ignored in the EF configuration. An <c>is_active</c>
    /// column sitting beside <c>returned_at</c> would be a second answer to a question the first
    /// column already settles, and the two could disagree.
    /// </summary>
    public bool IsActive => ReturnedAt is null;

    /// <summary>
    /// Whether a loan is overdue at a given instant: still out, and past its due date.
    ///
    /// Expressed as an expression rather than a method, and that is the whole point. A method body
    /// cannot be translated into SQL, so a query filtering on "overdue" would have to restate
    /// <c>ReturnedAt == null &amp;&amp; DueAt &lt; now</c> in its <c>Where</c> clause — the same rule
    /// written twice, in two languages, drifting the day one of them changes. Handing the database
    /// this expression means the rule is defined once, here, in the aggregate that owns it.
    ///
    /// <paramref name="now"/> is a parameter rather than a captured constant so the provider renders
    /// it as a query parameter. Baking the instant into the tree would produce a different SQL
    /// string on every request and a new entry in the query cache each time — unbounded growth
    /// driven by the clock.
    ///
    /// Overdue remains derived, never stored: a column would be wrong for the whole interval
    /// between a loan falling due and whatever job got round to updating it.
    /// </summary>
    public static Expression<Func<Loan, bool>> OverdueAt(DateTimeOffset now) =>
        loan => loan.ReturnedAt == null && loan.DueAt < now;

    /// <summary>
    /// Opens a loan. The only way a Loan comes into existence, so every rule about whether
    /// borrowing is allowed is in one place and cannot be bypassed by a caller that forgets.
    ///
    /// Takes the <paramref name="copy"/> and <paramref name="member"/> aggregates rather than their
    /// ids so the guards read as prose — <c>if (!member.CanBorrow)</c> — and so a caller cannot
    /// invent an id it never loaded. It stores only their ids.
    ///
    /// The two facts it cannot determine for itself are passed in, because an aggregate does not
    /// query: how many active loans the member holds, and whether this copy is already out.
    /// </summary>
    /// <param name="copyHasActiveLoan">
    /// True when the copy is currently out. Named so its polarity is unmistakable — the port that
    /// answers it is called <c>HasActiveLoanForCopyAsync</c> for the same reason.
    /// </param>
    /// <param name="now">
    /// From an injected <c>TimeProvider</c>, never <c>DateTime.UtcNow</c>, which is what makes the
    /// due-date arithmetic testable without waiting for a clock.
    /// </param>
    public static Result<Loan> Open(
        BookCopy copy,
        Member member,
        int memberActiveLoanCount,
        bool copyHasActiveLoan,
        DateTimeOffset now)
    {
        // Guard order is deliberate: the caller's own eligibility first, then the resource's.
        // A suspended member should be told they are suspended, not that the book is out.
        if (!member.CanBorrow)
        {
            return LoanErrors.MemberSuspended();
        }

        // This limit can be raced — two concurrent borrows can both read a count of four, and the
        // member ends up holding six. That is accepted, and the asymmetry with the check below is
        // the interesting part of this design: a member briefly over their limit is a policy
        // annoyance that a librarian can unwind, while the same physical book promised to two
        // people is a failure the library cannot honour. Only the second one gets a database
        // constraint behind it, because only the second one is worth the cost of having one.
        if (memberActiveLoanCount >= LoanPolicy.MaxActiveLoansPerMember)
        {
            return LoanErrors.MemberAtLoanLimit();
        }

        // Checked last, on purpose. This is the one guard the database also enforces, via a partial
        // unique index on (book_copy_id) WHERE returned_at IS NULL. Two requests can both pass this
        // line microseconds apart; the index is what decides which INSERT survives, and the unit of
        // work translates its violation back into this identical error. Keeping it last makes the
        // "same answer whichever layer noticed" story about a single, final guard.
        if (copyHasActiveLoan)
        {
            return LoanErrors.CopyAlreadyOnLoan();
        }

        return Result<Loan>.Success(new Loan(
            Guid.CreateVersion7(),
            copy.Id,
            member.Id,
            now,
            now.AddDays(LoanPolicy.LoanPeriodDays)));
    }

    /// <summary>
    /// Records the copy coming back.
    ///
    /// Returns the non-generic <see cref="Result"/> because it mutates the loan the caller already
    /// holds — <c>Result&lt;Loan&gt;</c> would imply a new aggregate had been produced.
    ///
    /// A second return is a <see cref="DomainErrorKind.Conflict"/>, not a silent success. Two
    /// concurrent returns can both pass this guard and both write, which is accepted: unlike the
    /// borrow race there is no token or index that could arbitrate it, and the outcome is
    /// idempotent in substance — the loan ends up returned either way, with the two candidate
    /// timestamps differing by microseconds. Nothing is promised twice, which is the difference
    /// that matters.
    ///
    /// The race that <i>is</i> arbitrated, and it is the more interesting one: a return running
    /// concurrently with a re-borrow of the same copy. The new loan's INSERT cannot land while this
    /// one still has a null <see cref="ReturnedAt"/>, so the partial index settles a race across
    /// two different operations rather than merely between two identical ones.
    /// </summary>
    public Result Return(DateTimeOffset now)
    {
        if (ReturnedAt is not null)
        {
            return LoanErrors.AlreadyReturned();
        }

        ReturnedAt = now;

        return Result.Success();
    }
}
