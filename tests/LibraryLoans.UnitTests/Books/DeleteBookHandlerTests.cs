using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryLoans.UnitTests.Books;

/// <summary>
/// Deleting a title, and the two refusals that differ only in what a caller should do about them.
///
/// The distinction is the point: a copy currently out means "try again once it is back", while any
/// lending history at all means never. Both are 409s, so a test asserting only the status code would
/// pass with the two collapsed into one, which would lose the only part of the answer a client can
/// act on.
/// </summary>
public sealed class DeleteBookHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBookRepository _books = new();
    private readonly InMemoryBookCopyRepository _copies = new();
    private readonly InMemoryLoanRepository _loans = new();

    [Fact]
    public async Task Removes_the_title_and_all_of_its_copies()
    {
        var book = ABook();
        _books.Seed(book);
        _copies.Seed(ACopy(book, "COPY-0001"));
        _copies.Seed(ACopy(book, "COPY-0002"));

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(book.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(book, _books.Removed);
        Assert.Equal(2, _copies.Removed.Count);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reports_an_unknown_title_as_not_found_without_committing()
    {
        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.not_found", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>Retryable: the copy will come back.</summary>
    [Fact]
    public async Task Refuses_while_a_copy_is_out_and_removes_nothing()
    {
        var book = ABook();
        _books.Seed(book);
        _copies.Seed(ACopy(book, "COPY-0001"));
        _loans.BookHasActiveLoan = true;
        _loans.BookHasAnyLoan = true;

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(book.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.copy_on_loan", result.Error.Code);
        Assert.Empty(_books.Removed);
        Assert.Empty(_copies.Removed);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>
    /// Permanent, and the case that would otherwise be a 500. Every copy is back, so a precondition
    /// that only looked for active loans would let this through, and the foreign key from the
    /// returned loan to the copy would reject the statement.
    /// </summary>
    [Fact]
    public async Task Refuses_when_the_title_has_lending_history_even_though_every_copy_is_back()
    {
        var book = ABook();
        _books.Seed(book);
        _copies.Seed(ACopy(book, "COPY-0001"));
        _loans.BookHasActiveLoan = false;
        _loans.BookHasAnyLoan = true;

        var unitOfWork = new RecordingUnitOfWork();

        var result = await CreateHandler(unitOfWork).HandleAsync(book.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.has_loan_history", result.Error.Code);
        Assert.Empty(_books.Removed);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    /// <summary>
    /// A borrow landing between the precondition and the write is decided by the foreign key, and
    /// the unit of work reports it as the same retryable conflict the check produces, so the caller
    /// cannot tell which layer noticed.
    /// </summary>
    [Fact]
    public async Task Reports_a_race_lost_at_the_database_as_the_retryable_conflict()
    {
        var book = ABook();
        _books.Seed(book);
        _copies.Seed(ACopy(book, "COPY-0001"));

        var unitOfWork = new RecordingUnitOfWork(BookErrors.CopyOnLoan());

        var result = await CreateHandler(unitOfWork).HandleAsync(book.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.copy_on_loan", result.Error.Code);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private DeleteBookHandler CreateHandler(RecordingUnitOfWork unitOfWork) =>
        new(_books, _copies, _loans, unitOfWork, NullLogger<DeleteBookHandler>.Instance);

    private static Book ABook()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);

        var book = Book.Create(isbn.Value, "The Hobbit", "J. R. R. Tolkien", 1937, Now);
        Assert.True(book.IsSuccess);

        return book.Value;
    }

    private static BookCopy ACopy(Book book, string barcodeValue)
    {
        var barcode = Barcode.Create(barcodeValue);
        Assert.True(barcode.IsSuccess);

        return BookCopy.Add(book, barcode.Value);
    }
}
