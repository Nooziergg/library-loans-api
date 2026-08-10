using LibraryLoans.Domain.Copies;

namespace LibraryLoans.Application.Copies;

public interface IBookCopyRepository
{
    Task<bool> ExistsWithBarcodeAsync(Barcode barcode, CancellationToken cancellationToken);

    /// <summary>A read, like <see cref="Members.IMemberRepository.GetByIdAsync"/> — untracked.</summary>
    Task<BookCopy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(BookCopy copy);
}
