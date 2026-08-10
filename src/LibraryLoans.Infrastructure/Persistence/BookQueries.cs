using LibraryLoans.Application.Books;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// The read-side adapter for books: no change tracking, and only the columns the response
/// actually contains.
/// </summary>
internal sealed class BookQueries(LibraryDbContext dbContext) : IBookQueries
{
    public async Task<BookResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // The rule every read path in this project follows: select straight into the response
        // type, so the SQL carries only the columns the response contains. Never materialise an
        // entity and map it in memory — that is the difference between a narrow SELECT and
        // dragging a graph across the wire to discard most of it.
        //
        // Reading book.Isbn.Value inside the projection is safe even though Isbn is mapped
        // through a ValueConverter. EF Core translates book.Isbn to the isbn column, applies the
        // converter during materialisation, and evaluates the .Value access client-side while
        // constructing the record — client evaluation is still permitted in the top-level
        // projection, which is the one place it was deliberately kept. Verified against a real
        // PostgreSQL by the integration suite, not assumed: an earlier version of this method
        // projected to an anonymous type first on the belief that the direct form would throw.
        // It does not.
        //
        // The alternative worth naming, since it is the obvious one: mapping Isbn with OwnsOne
        // would make the access translate in SQL rather than client-side. Rejected because it
        // turns a value object into an owned entity with its own identity and lifecycle
        // semantics, which is a large concession for one property access that costs nothing
        // here.
        return await dbContext.Books
            .AsNoTracking()
            .Where(book => book.Id == id)
            .Select(book => new BookResponse(
                book.Id,
                book.Isbn.Value,
                book.Title,
                book.Author,
                book.PublishedYear))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
