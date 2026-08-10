using LibraryLoans.Application.Common;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.Application.Books;

/// <summary>
/// Searching the catalogue. Thin, like the other read handler, and present for the same reason: so
/// the endpoint stays a pure HTTP adapter with nowhere to accumulate behaviour.
///
/// Note what it does not do: validate the sort field or clamp the page size. Both are shape
/// concerns, enforced by DataAnnotations on the request DTO before this is called, so they produce
/// 400 rather than 422. Doing it here as well would put one rule in two places and answer the same
/// mistake with two different status codes depending on which check ran first.
/// </summary>
public sealed class SearchBooksHandler(IBookQueries books)
{
    public async Task<Result<PagedResponse<BookResponse>>> HandleAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken) =>
        Result<PagedResponse<BookResponse>>.Success(await books.SearchAsync(query, cancellationToken));
}
