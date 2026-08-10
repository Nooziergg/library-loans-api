using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;

namespace LibraryLoans.UnitTests.Loans;

/// <summary>
/// The rules that decide whether a copy may leave the building, and what happens when it comes back.
/// The clock is a parameter throughout, so none of these depend on the wall clock.
/// </summary>
public sealed class LoanTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Opens_a_loan_due_after_the_policy_period()
    {
        var result = Loan.Open(ACopy(), AMember(), memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.LoanedAt);
        Assert.Equal(Now.AddDays(LoanPolicy.LoanPeriodDays), result.Value.DueAt);
        Assert.Null(result.Value.ReturnedAt);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Records_which_copy_and_member_the_loan_is_for()
    {
        var copy = ACopy();
        var member = AMember();

        var result = Loan.Open(copy, member, memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(copy.Id, result.Value.BookCopyId);
        Assert.Equal(member.Id, result.Value.MemberId);
    }

    /// <summary>Invariant 1, in the aggregate. The database enforces it again where it counts.</summary>
    [Fact]
    public void Refuses_a_copy_that_is_already_on_loan()
    {
        var result = Loan.Open(ACopy(), AMember(), memberActiveLoanCount: 0, copyHasActiveLoan: true, now: Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.copy.already_on_loan", result.Error.Code);
    }

    [Fact]
    public void Refuses_a_suspended_member()
    {
        var member = AMember();
        Assert.True(member.Suspend().IsSuccess);

        var result = Loan.Open(ACopy(), member, memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.member.suspended", result.Error.Code);
    }

    [Fact]
    public void Refuses_a_member_already_at_the_loan_limit()
    {
        var result = Loan.Open(
            ACopy(),
            AMember(),
            memberActiveLoanCount: LoanPolicy.MaxActiveLoansPerMember,
            copyHasActiveLoan: false,
            now: Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.member.at_loan_limit", result.Error.Code);
    }

    [Fact]
    public void Allows_a_member_one_below_the_limit()
    {
        var result = Loan.Open(
            ACopy(),
            AMember(),
            memberActiveLoanCount: LoanPolicy.MaxActiveLoansPerMember - 1,
            copyHasActiveLoan: false,
            now: Now);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Guard order is part of the contract: a suspended member at their limit borrowing an
    /// already-loaned copy should be told they are suspended. Telling them the copy is out would
    /// send them to find another one, and the next attempt would fail for the same real reason.
    /// </summary>
    [Fact]
    public void Reports_the_callers_own_ineligibility_before_the_copys()
    {
        var member = AMember();
        Assert.True(member.Suspend().IsSuccess);

        var result = Loan.Open(
            ACopy(),
            member,
            memberActiveLoanCount: LoanPolicy.MaxActiveLoansPerMember,
            copyHasActiveLoan: true,
            now: Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.member.suspended", result.Error.Code);
    }

    [Fact]
    public void Records_the_return()
    {
        var loan = AnOpenLoan();
        var returnedAt = Now.AddDays(3);

        var result = loan.Return(returnedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(returnedAt, loan.ReturnedAt);
        Assert.False(loan.IsActive);
    }

    /// <summary>
    /// Invariant 6. A silent success here would tell a caller their book was returned when the
    /// library's records say it was returned at some other moment — the second call is describing an
    /// event that did not happen.
    /// </summary>
    [Fact]
    public void Refuses_a_second_return()
    {
        var loan = AnOpenLoan();
        Assert.True(loan.Return(Now.AddDays(3)).IsSuccess);

        var second = loan.Return(Now.AddDays(4));

        Assert.False(second.IsSuccess);
        Assert.Equal("loan.already_returned", second.Error.Code);
    }

    [Fact]
    public void Keeps_the_first_return_instant_when_a_second_is_refused()
    {
        var loan = AnOpenLoan();
        var firstReturn = Now.AddDays(3);
        Assert.True(loan.Return(firstReturn).IsSuccess);

        loan.Return(Now.AddDays(4));

        Assert.Equal(firstReturn, loan.ReturnedAt);
    }

    private static Loan AnOpenLoan()
    {
        var loan = Loan.Open(ACopy(), AMember(), memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);
        Assert.True(loan.IsSuccess);

        return loan.Value;
    }

    private static BookCopy ACopy()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);

        var book = Book.Create(isbn.Value, "The Hobbit", "J. R. R. Tolkien", 1937, Now);
        Assert.True(book.IsSuccess);

        var barcode = Barcode.Create("COPY-0001");
        Assert.True(barcode.IsSuccess);

        return BookCopy.Add(book.Value, barcode.Value);
    }

    private static Member AMember()
    {
        var number = MembershipNumber.Create("M00000001");
        Assert.True(number.IsSuccess);

        var member = Member.Register(number.Value, "A Borrower", "borrower@example.test");
        Assert.True(member.IsSuccess);

        return member.Value;
    }
}
