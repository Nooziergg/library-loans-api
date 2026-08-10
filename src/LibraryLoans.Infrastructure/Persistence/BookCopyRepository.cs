using LibraryLoans.Application.Copies;
using LibraryLoans.Domain.Copies;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class BookCopyRepository(LibraryDbContext dbContext) : IBookCopyRepository
{
    public Task<bool> ExistsWithBarcodeAsync(Barcode barcode, CancellationToken cancellationToken) =>
        dbContext.BookCopies
            .AsNoTracking()
            .AnyAsync(copy => copy.Barcode == barcode, cancellationToken);

    /// <summary>Untracked, for the same reason as <see cref="MemberRepository.GetByIdAsync"/>.</summary>
    public Task<BookCopy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.BookCopies
            .AsNoTracking()
            .FirstOrDefaultAsync(copy => copy.Id == id, cancellationToken);

    /// <summary>Tracked, because these are about to be removed.</summary>
    public async Task<IReadOnlyList<BookCopy>> FindAllForBookForUpdateAsync(
        Guid bookId,
        CancellationToken cancellationToken) =>
        await dbContext.BookCopies
            .Where(copy => copy.BookId == bookId)
            .ToListAsync(cancellationToken);

    public void Add(BookCopy copy) => dbContext.BookCopies.Add(copy);

    public void RemoveRange(IReadOnlyList<BookCopy> copies) => dbContext.BookCopies.RemoveRange(copies);
}
