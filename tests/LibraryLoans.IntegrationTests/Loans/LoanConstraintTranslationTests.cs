using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Loans;
using LibraryLoans.Application.Members;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.IntegrationTests.Loans;

/// <summary>
/// Proves that each unique constraint added in this phase is reported as the domain conflict it
/// represents rather than as a server fault.
///
/// These exist because the concurrency test cannot prove any of them. If two racing requests happen
/// to serialise, the handler's own pre-check rejects the second one and the database is never
/// consulted — so the translation path never runs and the test passes anyway. Should a constraint
/// name in <c>DatabaseConstraints</c> ever drift from the name in the migration,
/// <c>UniqueConstraintTranslation</c> would return null, the unit of work would rethrow, clients
/// would start receiving 500s in place of 409s, and the rest of the suite would stay green.
///
/// Driving the repositories and unit of work directly removes the race and turns each constraint
/// name into a tested contract against a real PostgreSQL.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoanConstraintTranslationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;

    public LoanConstraintTranslationTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _factory = new LibraryApiFactory(_postgres.ConnectionString);
        _ = _factory.Services;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>
    /// The important one: the partial unique index rejecting a second active loan, surfaced as the
    /// same error the in-memory guard produces.
    /// </summary>
    [Fact]
    public async Task A_second_active_loan_on_one_copy_becomes_a_domain_conflict()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var loans = scope.ServiceProvider.GetRequiredService<ILoanRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var (copy, member) = await SeedCopyAndMemberAsync(scope.ServiceProvider, "COPY-0001", "M00000001");

        loans.Add(OpenLoan(copy, member));
        loans.Add(OpenLoan(copy, member));

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("loan.copy.already_on_loan", result.Error.Code);
    }

    [Fact]
    public async Task A_duplicate_barcode_becomes_a_domain_conflict()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var books = scope.ServiceProvider.GetRequiredService<IBookRepository>();
        var copies = scope.ServiceProvider.GetRequiredService<IBookCopyRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var book = ABook();
        books.Add(book);

        var barcode = Barcode.Create("COPY-0001");
        Assert.True(barcode.IsSuccess);

        copies.Add(BookCopy.Add(book, barcode.Value));
        copies.Add(BookCopy.Add(book, barcode.Value));

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book_copy.barcode.duplicate", result.Error.Code);
    }

    [Fact]
    public async Task A_duplicate_membership_number_becomes_a_domain_conflict()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<IMemberRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        members.Add(AMember("M00000001", "one@example.test"));
        members.Add(AMember("M00000001", "two@example.test"));

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("member.membership_number.duplicate", result.Error.Code);
    }

    private static async Task<(BookCopy Copy, Member Member)> SeedCopyAndMemberAsync(
        IServiceProvider services,
        string barcodeValue,
        string membershipNumber)
    {
        var books = services.GetRequiredService<IBookRepository>();
        var copies = services.GetRequiredService<IBookCopyRepository>();
        var members = services.GetRequiredService<IMemberRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var book = ABook();
        books.Add(book);

        var barcode = Barcode.Create(barcodeValue);
        Assert.True(barcode.IsSuccess);
        var copy = BookCopy.Add(book, barcode.Value);
        copies.Add(copy);

        var member = AMember(membershipNumber, "borrower@example.test");
        members.Add(member);

        var saved = await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.True(saved.IsSuccess);

        return (copy, member);
    }

    private static Book ABook()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);

        var book = Book.Create(isbn.Value, "The Hobbit", "J. R. R. Tolkien", 1937, Now);
        Assert.True(book.IsSuccess);

        return book.Value;
    }

    private static Member AMember(string membershipNumber, string email)
    {
        var number = MembershipNumber.Create(membershipNumber);
        Assert.True(number.IsSuccess);

        var member = Member.Register(number.Value, "A Borrower", email);
        Assert.True(member.IsSuccess);

        return member.Value;
    }

    private static Loan OpenLoan(BookCopy copy, Member member)
    {
        // copyHasActiveLoan is false on purpose: this test is about what the database does when the
        // in-memory guard has already been satisfied, which is exactly the situation a lost race
        // produces.
        var loan = Loan.Open(copy, member, memberActiveLoanCount: 0, copyHasActiveLoan: false, now: Now);
        Assert.True(loan.IsSuccess);

        return loan.Value;
    }
}
