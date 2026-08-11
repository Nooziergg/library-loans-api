using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;
using LibraryLoans.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryLoans.UnitTests.Books;

public sealed class CreateBookHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();

    private CreateBookHandler CreateHandler(RecordingUnitOfWork unitOfWork) =>
        new(_books, unitOfWork, new FixedTimeProvider(Now), NullLogger<CreateBookHandler>.Instance);

    private static CreateBookCommand AValidCommand() =>
        new("978-0-306-40615-7", "The Hobbit", "J. R. R. Tolkien", 1937);

    [Fact]
    public async Task Adds_a_book_and_commits_exactly_once()
    {
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(AValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("The Hobbit", result.Value.Title);
        Assert.Single(_books.Added);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    /// <summary>
    /// The response reports the canonical ISBN, not the hyphenated string the caller sent, so a
    /// client can store what it gets back and have it match on the next lookup.
    /// </summary>
    [Fact]
    public async Task Reports_the_canonical_isbn()
    {
        var result = await CreateHandler(new RecordingUnitOfWork())
            .HandleAsync(AValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("9780306406157", result.Value.Isbn);
    }

    [Fact]
    public async Task Rejects_an_invalid_isbn_without_touching_the_database()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var command = AValidCommand() with { Isbn = "9780306406158" };

        var result = await CreateHandler(unitOfWork).HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.checksum_failed", result.Error.Code);
        Assert.Empty(_books.Added);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Rejects_invalid_details_without_committing()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var command = AValidCommand() with { Title = "  " };

        var result = await CreateHandler(unitOfWork).HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.title.required", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Rejects_a_duplicate_isbn_without_committing()
    {
        var existing = Book.Create(
            Isbn.Create("9780306406157").Value,
            "The Hobbit",
            "J. R. R. Tolkien",
            1937,
            Now);
        _books.Seed(existing.Value);

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(AValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.duplicate", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>
    /// The duplicate is detected in a different layer here, the pre-check missed it and the
    /// database's unique index caught it, and the caller must not be able to tell the
    /// difference. Same code, same status, whichever path found it.
    /// </summary>
    [Fact]
    public async Task Reports_a_race_lost_at_the_database_identically_to_one_caught_in_advance()
    {
        var unitOfWork = new RecordingUnitOfWork(BookErrors.DuplicateIsbn());

        var result = await CreateHandler(unitOfWork).HandleAsync(AValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.duplicate", result.Error.Code);
        Assert.Equal(1, unitOfWork.SaveCount);
    }
}
