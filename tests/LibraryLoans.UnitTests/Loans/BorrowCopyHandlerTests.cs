using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using LibraryLoans.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryLoans.UnitTests.Loans;

/// <summary>
/// The plumbing between the repositories and <see cref="Loan.Open"/>.
///
/// These matter because every rule in <c>LoanTests</c> passes whether or not the handler wires the
/// flags up correctly. If it inverted <c>copyHasActiveLoan</c>, or counted the wrong member's loans,
/// or asked about the wrong copy, the domain tests would be perfectly green and the library would
/// lend the same book twice.
/// </summary>
public sealed class BorrowCopyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookCopyRepository _copies = new();
    private readonly InMemoryMemberRepository _members = new();
    private readonly InMemoryLoanRepository _loans = new();

    [Fact]
    public async Task Opens_a_loan_and_commits_exactly_once()
    {
        var (copy, member) = Arrange();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(new BorrowCopyCommand(member.Id, copy.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(copy.Id, result.Value.BookCopyId);
        Assert.Equal(member.Id, result.Value.MemberId);
        Assert.Single(_loans.Added);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>
    /// The polarity test. <c>HasActiveLoanForCopyAsync</c> returns true when the copy is out, and
    /// the handler passes that straight through: inverting it is a one-character change the
    /// compiler cannot object to, and this is what catches it.
    /// </summary>
    [Fact]
    public async Task Refuses_when_the_repository_reports_the_copy_is_already_out()
    {
        var (copy, member) = Arrange();
        _loans.Seed(AnActiveLoanFor(copy, member));

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(new BorrowCopyCommand(member.Id, copy.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.copy.already_on_loan", result.Error.Code);
        Assert.Empty(_loans.Added);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>Catches a transposition: asking about the member's id when it wanted the copy's.</summary>
    [Fact]
    public async Task Asks_about_the_copy_and_member_named_in_the_command()
    {
        var (copy, member) = Arrange();

        await CreateHandler(new RecordingUnitOfWork())
            .HandleAsync(new BorrowCopyCommand(member.Id, copy.Id), CancellationToken.None);

        Assert.Equal(copy.Id, _loans.CopyAskedAbout);
        Assert.Equal(member.Id, _loans.MemberAskedAbout);
    }

    [Fact]
    public async Task Reports_an_unknown_copy_as_not_found_without_committing()
    {
        var (_, member) = Arrange();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(new BorrowCopyCommand(member.Id, Guid.CreateVersion7()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book_copy.not_found", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reports_an_unknown_member_as_not_found_without_committing()
    {
        var (copy, _) = Arrange();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(new BorrowCopyCommand(Guid.CreateVersion7(), copy.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.not_found", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>
    /// The race lost at the database must be indistinguishable from the one caught in advance:
    /// same code, same status, whichever layer noticed.
    /// </summary>
    [Fact]
    public async Task Reports_a_race_lost_at_the_database_identically_to_one_caught_in_advance()
    {
        var (copy, member) = Arrange();
        var unitOfWork = new RecordingUnitOfWork(LoanErrors.CopyAlreadyOnLoan());

        var result = await CreateHandler(unitOfWork)
            .HandleAsync(new BorrowCopyCommand(member.Id, copy.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.copy.already_on_loan", result.Error.Code);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Uses_the_injected_clock_for_the_loan_and_due_dates()
    {
        var (copy, member) = Arrange();

        var result = await CreateHandler(new RecordingUnitOfWork())
            .HandleAsync(new BorrowCopyCommand(member.Id, copy.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.LoanedAt);
        Assert.Equal(Now.AddDays(LoanPolicy.LoanPeriodDays), result.Value.DueAt);
    }

    private BorrowCopyHandler CreateHandler(RecordingUnitOfWork unitOfWork) =>
        new(
            _copies,
            _members,
            _loans,
            unitOfWork,
            new FixedTimeProvider(Now),
            NullLogger<BorrowCopyHandler>.Instance);

    private (BookCopy Copy, Member Member) Arrange()
    {
        var copy = ACopy();
        var member = AMember();

        _copies.Seed(copy);
        _members.Seed(member);

        return (copy, member);
    }

    private static Loan AnActiveLoanFor(BookCopy copy, Member member)
    {
        var loan = Loan.Open(copy, member, memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);
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
