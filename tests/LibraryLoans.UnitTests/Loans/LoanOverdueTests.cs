using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;

namespace LibraryLoans.UnitTests.Loans;

/// <summary>
/// The overdue rule.
///
/// It exists as an expression so the database can apply it, which means the query layer and the
/// domain cannot disagree about what "overdue" is — there is only one definition. Testing it means
/// compiling that same expression here, so what these assertions exercise is exactly what
/// PostgreSQL is handed. Agreement between the two is true by construction rather than asserted.
/// </summary>
public sealed class LoanOverdueTests
{
    private static readonly DateTimeOffset LoanedAt = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = LoanedAt.AddDays(LoanPolicy.LoanPeriodDays);

    private static bool IsOverdueAt(Loan loan, DateTimeOffset now) =>
        Loan.OverdueAt(now).Compile()(loan);

    [Fact]
    public void A_loan_is_not_overdue_before_its_due_date()
    {
        Assert.False(IsOverdueAt(AnOpenLoan(), DueAt.AddDays(-1)));
    }

    /// <summary>
    /// Exactly at the due instant is not yet late. The rule is a strict comparison, and a boundary
    /// like this is where an off-by-one turns into a borrower being told they are overdue on the day
    /// the book is due back.
    /// </summary>
    [Fact]
    public void A_loan_is_not_overdue_at_the_exact_moment_it_falls_due()
    {
        Assert.False(IsOverdueAt(AnOpenLoan(), DueAt));
    }

    [Fact]
    public void A_loan_is_overdue_once_past_its_due_date()
    {
        Assert.True(IsOverdueAt(AnOpenLoan(), DueAt.AddTicks(1)));
        Assert.True(IsOverdueAt(AnOpenLoan(), DueAt.AddDays(30)));
    }

    /// <summary>
    /// A returned loan is never overdue, however long ago it was due. Overdue means the library is
    /// still waiting for the book — not that it came back late.
    /// </summary>
    [Fact]
    public void A_returned_loan_is_never_overdue()
    {
        var loan = AnOpenLoan();
        Assert.True(loan.Return(DueAt.AddDays(5)).IsSuccess);

        Assert.False(IsOverdueAt(loan, DueAt.AddDays(1)));
        Assert.False(IsOverdueAt(loan, DueAt.AddDays(365)));
    }

    private static Loan AnOpenLoan()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);

        var book = Book.Create(isbn.Value, "The Hobbit", "J. R. R. Tolkien", 1937, LoanedAt);
        Assert.True(book.IsSuccess);

        var barcode = Barcode.Create("COPY-0001");
        Assert.True(barcode.IsSuccess);

        var number = MembershipNumber.Create("M00000001");
        Assert.True(number.IsSuccess);

        var member = Member.Register(number.Value, "A Borrower", "borrower@example.test");
        Assert.True(member.IsSuccess);

        var loan = Loan.Open(
            BookCopy.Add(book.Value, barcode.Value),
            member.Value,
            memberActiveLoanCount: 0,
            copyHasActiveLoan: false,
            now: LoanedAt);

        Assert.True(loan.IsSuccess);

        return loan.Value;
    }
}
