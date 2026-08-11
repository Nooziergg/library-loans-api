using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.Application.Copies;

/// <summary>
/// The copies of one title.
///
/// An unknown book is a <b>404</b>, not an empty page. This is a subresource, so the question
/// "which copies does this title have" is meaningless if the title does not exist. Contrast
/// <c>GET /loans?memberId=...</c>, where an unknown id correctly yields an empty page because that is
/// a filter over a collection that exists either way.
/// </summary>
public sealed class ListCopiesOfBookHandler(IBookRepository books, IBookCopyQueries copies)
{
    public async Task<Result<PagedResponse<BookCopyResponse>>> HandleAsync(
        Guid bookId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(bookId, cancellationToken);
        if (book is null)
        {
            return BookErrors.NotFound(bookId);
        }

        return Result<PagedResponse<BookCopyResponse>>.Success(
            await copies.ListForBookAsync(bookId, page, pageSize, cancellationToken));
    }
}
