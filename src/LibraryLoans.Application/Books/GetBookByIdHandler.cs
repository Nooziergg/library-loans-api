using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.Application.Books;

/// <summary>
/// Reads a single title.
///
/// Thin today, and deliberately still present: translating "no row" into a described
/// <see cref="DomainError"/> happens here rather than in the endpoint, which keeps the endpoint
/// a pure HTTP adapter that knows only how to turn a result into a status code. The alternative
/// puts a small piece of application behaviour in the web layer, and that is exactly where it
/// gets duplicated inconsistently once there are a dozen endpoints.
/// </summary>
public sealed class GetBookByIdHandler(IBookQueries books)
{
    public async Task<Result<BookResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(id, cancellationToken);

        return book is null
            ? BookErrors.NotFound(id)
            : Result<BookResponse>.Success(book);
    }
}
