using LibraryLoans.Domain.Copies;

namespace LibraryLoans.Application.Copies;

public interface IBookCopyRepository
{
    Task<bool> ExistsWithBarcodeAsync(Barcode barcode, CancellationToken cancellationToken);

    /// <summary>A read, like <see cref="Members.IMemberRepository.GetByIdAsync"/> — untracked.</summary>
    Task<BookCopy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every copy of a title, tracked, so they can be removed with it. Deleting a book has to remove
    /// its copies explicitly: the foreign key is <c>Restrict</c>, so the database refuses to cascade
    /// — which is deliberate, because it makes removing rows always a decision the application took
    /// on purpose rather than a side effect of another one.
    /// </summary>
    Task<IReadOnlyList<BookCopy>> FindAllForBookForUpdateAsync(Guid bookId, CancellationToken cancellationToken);

    void Add(BookCopy copy);

    void RemoveRange(IReadOnlyList<BookCopy> copies);
}
