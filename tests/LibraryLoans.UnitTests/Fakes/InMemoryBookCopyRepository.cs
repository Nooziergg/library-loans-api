using LibraryLoans.Application.Copies;
using LibraryLoans.Domain.Copies;

namespace LibraryLoans.UnitTests.Fakes;

internal sealed class InMemoryBookCopyRepository : IBookCopyRepository
{
    private readonly List<BookCopy> _preexisting = [];
    private readonly List<BookCopy> _added = [];

    public IReadOnlyList<BookCopy> Added => _added;

    public Task<bool> ExistsWithBarcodeAsync(Barcode barcode, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).Any(copy => copy.Barcode == barcode));

    public Task<BookCopy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).FirstOrDefault(copy => copy.Id == id));

    public Task<IReadOnlyList<BookCopy>> FindAllForBookForUpdateAsync(
        Guid bookId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BookCopy>>(
            _preexisting.Concat(_added).Where(copy => copy.BookId == bookId).ToList());

    public void Add(BookCopy copy) => _added.Add(copy);

    public void RemoveRange(IReadOnlyList<BookCopy> copies)
    {
        Removed.AddRange(copies);

        foreach (var copy in copies)
        {
            _preexisting.Remove(copy);
            _added.Remove(copy);
        }
    }

    public List<BookCopy> Removed { get; } = [];

    public void Seed(BookCopy copy) => _preexisting.Add(copy);
}
