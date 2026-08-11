using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using LibraryLoans.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryLoans.UnitTests.Loans;

/// <summary>
/// The return path.
///
/// The first test here is the one that earns the file. This codebase asserts, in its architecture
/// documentation and in the reasoning behind every date in the domain, that time is injected and
/// <c>DateTime.UtcNow</c> appears nowhere. Before this test existed, replacing the handler's
/// <c>timeProvider.GetUtcNow()</c> with <c>DateTime.UtcNow</c> left the entire suite green: the
/// return would still be recorded, the integration tests would still pass, and the only claim that
/// broke would be one nothing checked.
/// </summary>
public sealed class ReturnLoanHandlerTests
{
    private static readonly DateTimeOffset LoanedAt = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReturnedAt = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryLoanRepository _loans = new();

    [Fact]
    public async Task Records_the_return_at_the_injected_clocks_instant()
    {
        var loan = AnOpenLoan();
        _loans.Seed(loan);

        var result = await CreateHandler(new RecordingUnitOfWork())
            .HandleAsync(loan.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnedAt, result.Value.ReturnedAt);
    }

    [Fact]
    public async Task Commits_exactly_once()
    {
        var loan = AnOpenLoan();
        _loans.Seed(loan);
        var unitOfWork = new RecordingUnitOfWork();

        await CreateHandler(unitOfWork).HandleAsync(loan.Id, CancellationToken.None);

        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>
    /// The guard whose absence would be a <see cref="NullReferenceException"/> rather than a
    /// described 404.
    /// </summary>
    [Fact]
    public async Task Reports_an_unknown_loan_as_not_found_without_committing()
    {
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.not_found", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Refuses_a_second_return_without_committing()
    {
        var loan = AnOpenLoan();
        Assert.True(loan.Return(LoanedAt.AddDays(1)).IsSuccess);
        _loans.Seed(loan);

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(loan.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.already_returned", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private ReturnLoanHandler CreateHandler(RecordingUnitOfWork unitOfWork) =>
        new(_loans, unitOfWork, new FixedTimeProvider(ReturnedAt), NullLogger<ReturnLoanHandler>.Instance);

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
