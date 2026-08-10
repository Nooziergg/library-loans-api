using LibraryLoans.Application.Common;
using LibraryLoans.Application.Copies;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class BookCopyQueries(LibraryDbContext dbContext) : IBookCopyQueries
{
    public async Task<PagedResponse<BookCopyResponse>> ListForBookAsync(
        Guid bookId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var copies = dbContext.BookCopies
            .AsNoTracking()
            .Where(copy => copy.BookId == bookId);

        var totalCount = await copies.CountAsync(cancellationToken);

        var items = await copies
            .OrderBy(copy => copy.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(copy => new BookCopyResponse(copy.Id, copy.BookId, copy.Barcode.Value))
            .ToListAsync(cancellationToken);

        return new PagedResponse<BookCopyResponse>(items, page, pageSize, totalCount);
    }
}
