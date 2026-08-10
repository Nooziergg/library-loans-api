using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.IntegrationTests.Books;

/// <summary>
/// Proves that the database's uniqueness ruling is reported as a domain conflict rather than a
/// server fault.
///
/// This test exists because the concurrent-create test cannot prove it. If those two requests
/// happen to serialise, the handler's own pre-check catches the second one and the database is
/// never consulted — the test passes without the translation path ever running. So if the
/// constraint name in <c>DatabaseConstraints</c> ever drifted from the name in the migration,
/// <c>UniqueConstraintTranslation.Translate</c> would return null, <c>UnitOfWork</c> would
/// rethrow, clients would start getting 500s instead of 409s, and the suite would stay green.
///
/// Driving the repository and unit of work directly removes the race and makes the constraint
/// name a tested contract against a real PostgreSQL. The same mechanism carries the loan
/// invariant in P2, where a silent 500 in place of a 409 is precisely the failure this project
/// exists to avoid.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UniqueConstraintTranslationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;

    public UniqueConstraintTranslationTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _factory = new LibraryApiFactory(_postgres.ConnectionString);
        // Forces the host to build and migrate before the scope below is created.
        _ = _factory.Services;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task A_duplicate_isbn_rejected_by_the_index_becomes_a_domain_conflict()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var books = scope.ServiceProvider.GetRequiredService<IBookRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        books.Add(ABook("The Hobbit"));
        books.Add(ABook("The Hobbit, again"));

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.duplicate", result.Error.Code);
    }

    // A literal instant rather than DateTimeOffset.UtcNow. Nothing here depends on the current
    // time, and the codebase's position is that the wall clock is injected, never read — a
    // stray UtcNow in a test undercuts that argument for no benefit.
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Book ABook(string title)
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);

        var book = Book.Create(isbn.Value, title, "J. R. R. Tolkien", 1937, Now);
        Assert.True(book.IsSuccess);

        return book.Value;
    }
}
