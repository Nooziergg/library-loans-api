using System.Linq.Expressions;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;
using LibraryLoans.Domain.Books;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// The read-side adapter for books: no change tracking, and only the columns the response contains.
/// </summary>
internal sealed class BookQueries(LibraryDbContext dbContext) : IBookQueries
{
    /// <summary>
    /// PostgreSQL's default escape character for LIKE patterns. Named because it appears twice and
    /// because a bare <c>"\\"</c> at a call site reads like a typo.
    /// </summary>
    private const string LikeEscape = "\\";

    /// <summary>
    /// Sort fields a caller may name, mapped to what they actually order by.
    ///
    /// This is an allowlist rather than a lookup that falls back to something sensible: a name not
    /// in this dictionary is rejected before the request reaches the application, by
    /// <c>[AllowedValues]</c> on the request DTO. Keep the two in step: the attribute is what
    /// produces the 400, and this is what produces the SQL.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<Book, object>>> SortableFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = book => book.Title,
            ["author"] = book => book.Author,
            ["publishedYear"] = book => book.PublishedYear,
            ["isbn"] = book => book.Isbn,
        };

    public Task<BookResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        // Straight into the response type, so the SQL carries only the columns the response
        // contains and no entity is materialised or tracked. Reading book.Isbn.Value inside the
        // projection is safe despite the value converter: EF maps book.Isbn to the column, applies
        // the converter on materialisation, and evaluates .Value client-side while constructing the
        // record: client evaluation is still permitted in the top-level projection.
        dbContext.Books
            .AsNoTracking()
            .Where(book => book.Id == id)
            .Select(book => new BookResponse(
                book.Id,
                book.Isbn.Value,
                book.Title,
                book.Author,
                book.PublishedYear))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<BookResponse>> SearchAsync(
        BookSearchQuery query,
        CancellationToken cancellationToken)
    {
        var books = dbContext.Books.AsNoTracking();

        books = ApplySearch(books, query.Search);
        books = ApplyAvailability(books, query.AvailableOnly);

        // Counted before paging, against the same filters. A second round trip, and the only way to
        // tell a client how many pages there are.
        var totalCount = await books.CountAsync(cancellationToken);

        var items = await ApplySort(books, query.SortBy, query.Descending)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(book => new BookResponse(
                book.Id,
                book.Isbn.Value,
                book.Title,
                book.Author,
                book.PublishedYear))
            .ToListAsync(cancellationToken);

        return new PagedResponse<BookResponse>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Matches a term against the ISBN, the title or the author.
    ///
    /// The ISBN branch exists because stored ISBNs are canonical 13-digit forms. A caller searching
    /// for the number printed on the book (hyphenated, or the ISBN-10 of an older edition) would
    /// match nothing at all if the term were treated as text. Running it through the value object
    /// first turns any spelling of an ISBN into the one stored form, and that branch is an equality
    /// match served by the unique index rather than a scan.
    /// </summary>
    private static IQueryable<Book> ApplySearch(IQueryable<Book> books, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return books;
        }

        var term = search.Trim();

        var isbn = Isbn.Create(term);
        if (isbn.IsSuccess)
        {
            return books.Where(book => book.Isbn == isbn.Value);
        }

        // The term is caller-supplied and goes into a LIKE pattern, where % and _ are wildcards.
        // Unescaped, "%" alone would match the entire catalogue, and a term alternating % with
        // literals turns matching pathological: a cheap way to burn CPU on an endpoint that is
        // deliberately unauthenticated.
        var pattern = $"%{EscapeLikePattern(term)}%";

        return books.Where(book =>
            EF.Functions.ILike(book.Title, pattern, LikeEscape) ||
            EF.Functions.ILike(book.Author, pattern, LikeEscape));
    }

    /// <summary>
    /// Titles with at least one copy not currently out.
    ///
    /// Expressed as a correlated subquery rather than through a navigation property, because
    /// <see cref="Book"/> deliberately has no copies collection: adding one to make this read
    /// nicely would invite loading a graph on every read of a book.
    ///
    /// The inner test is served by <c>ix_loans_active_copy</c>: the same partial unique index that
    /// makes "a copy cannot be on two active loans" true. That is why there is no availability
    /// column on a copy to keep in sync: the index that enforces the invariant is the index that
    /// answers this question.
    /// </summary>
    private IQueryable<Book> ApplyAvailability(IQueryable<Book> books, bool availableOnly)
    {
        // Omitted entirely rather than compared against the flag: `where ... == availableOnly`
        // would leave a subquery in the plan for every request that did not ask for it.
        if (!availableOnly)
        {
            return books;
        }

        return books.Where(book => dbContext.BookCopies.Any(copy =>
            copy.BookId == book.Id &&
            !dbContext.Loans.Any(loan => loan.BookCopyId == copy.Id && loan.ReturnedAt == null)));
    }

    /// <summary>
    /// Orders the results, always ending on the primary key.
    ///
    /// The tiebreaker is not decoration. <c>ORDER BY title</c> alone leaves rows with equal titles
    /// in an order PostgreSQL does not define, and with <c>LIMIT/OFFSET</c> that means a row can
    /// appear on two pages or on none: a bug invisible at small volumes and impossible to
    /// reproduce once reported.
    ///
    /// The default is the key itself, which for version-7 GUIDs means insertion order. "No sort
    /// specified" therefore means chronological rather than arbitrary.
    /// </summary>
    private static IQueryable<Book> ApplySort(IQueryable<Book> books, string? sortBy, bool descending)
    {
        if (string.IsNullOrWhiteSpace(sortBy) || !SortableFields.TryGetValue(sortBy, out var keySelector))
        {
            return descending
                ? books.OrderByDescending(book => book.Id)
                : books.OrderBy(book => book.Id);
        }

        return descending
            ? books.OrderByDescending(keySelector).ThenBy(book => book.Id)
            : books.OrderBy(keySelector).ThenBy(book => book.Id);
    }

    private static string EscapeLikePattern(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
