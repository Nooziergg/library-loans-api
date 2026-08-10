using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;

namespace LibraryLoans.UnitTests.Fakes;

/// <summary>
/// A hand-written stand-in for the book repository.
///
/// No mocking framework. The port has two members, so a fake that actually stores books is
/// shorter than the setup calls a mock would need, and it reads as the thing it replaces rather
/// than as a script of expected interactions. It also cannot drift out of sync with the
/// interface, because the compiler checks it.
/// </summary>
internal sealed class InMemoryBookRepository : IBookRepository
{
    private readonly List<Book> _preexisting = [];
    private readonly List<Book> _added = [];

    /// <summary>
    /// Only what the code under test staged — deliberately not including books put there by
    /// <see cref="Seed"/>. Counting both would make <c>Assert.Single(Added)</c> pass in a test
    /// that seeded one book and added none, which is the exact assertion a handler that forgot
    /// to save should fail.
    /// </summary>
    public IReadOnlyList<Book> Added => _added;

    public Task<bool> ExistsWithIsbnAsync(Isbn isbn, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).Any(book => book.Isbn == isbn));

    public Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).FirstOrDefault(book => book.Id == id));

    public void Add(Book book) => _added.Add(book);

    /// <summary>Puts a book in the repository as though it were already there.</summary>
    public void Seed(Book book) => _preexisting.Add(book);
}
