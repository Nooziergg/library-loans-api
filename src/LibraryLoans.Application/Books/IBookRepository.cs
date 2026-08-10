using LibraryLoans.Domain.Books;

namespace LibraryLoans.Application.Books;

/// <summary>
/// The write side of book persistence: loads aggregates so they can enforce their own rules,
/// and stages new ones.
///
/// Note what is absent. There is no <c>GetAll</c>, and nothing here returns
/// <c>IQueryable&lt;Book&gt;</c>. Handing a queryable across this boundary would let any caller
/// compose an arbitrary query against the write model, and the decision about what SQL runs
/// would move out of the layer that owns it. Reads live behind
/// <see cref="IBookQueries"/> instead.
/// </summary>
public interface IBookRepository
{
    Task<bool> ExistsWithIsbnAsync(Isbn isbn, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a title so that another aggregate can prove it exists — adding a copy takes the Book
    /// rather than a <c>BookId</c>, so a caller cannot attach a copy to a title it invented. A read,
    /// so the implementation does not track it.
    /// </summary>
    Task<Book?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new book. Synchronous by design — this only marks the aggregate for insertion;
    /// nothing reaches the database until
    /// <see cref="Abstractions.IUnitOfWork.SaveChangesAsync"/> runs, and an <c>async</c>
    /// signature here would imply otherwise.
    /// </summary>
    /// <summary>Tracked, because the caller is about to change or remove what it loaded.</summary>
    Task<Book?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(Book book);

    void Remove(Book book);
}
