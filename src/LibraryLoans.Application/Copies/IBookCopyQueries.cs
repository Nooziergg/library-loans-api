using LibraryLoans.Application.Common;

namespace LibraryLoans.Application.Copies;

/// <summary>
/// The read side for physical copies.
/// </summary>
public interface IBookCopyQueries
{
    /// <summary>
    /// The copies of one title. Returns a page rather than a bare list, because "how many copies
    /// does a title have" has no small upper bound in a real library.
    /// </summary>
    Task<PagedResponse<BookCopyResponse>> ListForBookAsync(
        Guid bookId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
