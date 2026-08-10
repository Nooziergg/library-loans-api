using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// The write-side adapter for books.
/// </summary>
internal sealed class BookRepository(LibraryDbContext dbContext) : IBookRepository
{
    public Task<bool> ExistsWithIsbnAsync(Isbn isbn, CancellationToken cancellationToken) =>
        dbContext.Books
            // An existence check reads nothing it intends to modify, so tracking the result
            // would be pure cost. AnyAsync also stops the database at the first match instead
            // of counting or materialising a row.
            .AsNoTracking()
            .AnyAsync(book => book.Isbn == isbn, cancellationToken);

    public void Add(Book book) => dbContext.Books.Add(book);
}
