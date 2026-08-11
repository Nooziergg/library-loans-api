using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;

namespace LibraryLoans.UnitTests.Books;

public sealed class GetBookByIdHandlerTests
{
    [Fact]
    public async Task Returns_the_book_when_it_exists()
    {
        var id = Guid.CreateVersion7();
        var expected = new BookResponse(id, "9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);
        var handler = new GetBookByIdHandler(new StubBookQueries(expected));

        var result = await handler.HandleAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// "No row" becomes a described domain error here rather than a null the endpoint has to
    /// interpret, which is what keeps the 404 decision in one place instead of repeated at
    /// every read endpoint.
    /// </summary>
    [Fact]
    public async Task Reports_a_missing_book_as_not_found()
    {
        var handler = new GetBookByIdHandler(new StubBookQueries(null));

        var result = await handler.HandleAsync(Guid.CreateVersion7(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.not_found", result.Error.Code);
    }

    private sealed class StubBookQueries(BookResponse? response) : IBookQueries
    {
        public Task<BookResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(response);

        /// <summary>
        /// Not exercised here. Searching is a query built in SQL, so its behaviour is only
        /// meaningful against a real database. It is covered by the integration suite rather than
        /// by a stub that would only prove this stub works.
        /// </summary>
        public Task<PagedResponse<BookResponse>> SearchAsync(
            BookSearchQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Search is covered by the integration tests.");
    }
}
