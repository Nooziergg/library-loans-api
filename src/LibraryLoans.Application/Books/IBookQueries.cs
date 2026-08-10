namespace LibraryLoans.Application.Books;

/// <summary>
/// The read side of the catalogue. Separate from <see cref="IBookRepository"/> so that the two
/// properties every read path in this system needs — no change tracking, and projection to the
/// response shape in SQL rather than materializing an entity graph and mapping it in memory —
/// are structural rather than something each author has to remember.
///
/// This is interface segregation, not CQRS. There is one database, one model, and no eventual
/// consistency anywhere; the split buys a guarantee about how reads are written, nothing more.
/// </summary>
public interface IBookQueries
{
    Task<BookResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
