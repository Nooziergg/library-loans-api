using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Books;

/// <summary>
/// A catalogue title — the work, not a physical object. The things a borrower actually carries
/// home are <c>BookCopy</c> instances, added in P2.
/// </summary>
public sealed class Book
{
    public const int TitleMaxLength = 200;
    public const int AuthorMaxLength = 150;

    /// <summary>
    /// Gutenberg's press, near enough. The point of a floor is not historical precision — it
    /// is that <c>145</c> and <c>19999</c> are typos rather than years, and a system that
    /// accepts them will be asked about them later.
    /// </summary>
    public const int EarliestPublishedYear = 1450;

    /// <summary>Materialization path for the ORM only. See the <c>= null!</c> note below.</summary>
    private Book()
    {
    }

    private Book(Guid id, Isbn isbn, string title, string author, int publishedYear)
    {
        Id = id;
        Isbn = isbn;
        Title = title;
        Author = author;
        PublishedYear = publishedYear;
    }

    public Guid Id { get; private set; }

    // The `= null!` on these three is not a shrug at nullability. Every path that creates a
    // Book assigns them, and the ORM assigns them when reading a row; the compiler simply
    // cannot see either. Making the properties nullable instead would push a `!` to every use
    // site in the codebase to buy nothing, which is the strictly worse trade.
    public Isbn Isbn { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Author { get; private set; } = null!;

    public int PublishedYear { get; private set; }

    /// <summary>
    /// The only way to bring a Book into existence. Takes an already-parsed
    /// <paramref name="isbn"/> because validating it is <see cref="Isbn"/>'s job, and doing it
    /// in one place means it cannot be done differently in two.
    /// </summary>
    /// <param name="now">
    /// Supplied by the caller from an injected <c>TimeProvider</c> rather than read from
    /// <c>DateTime.UtcNow</c> here. That is what makes "a book cannot be published in the
    /// future" a deterministic test instead of one that depends on the wall clock.
    /// </param>
    public static Result<Book> Create(Isbn isbn, string? title, string? author, int publishedYear, DateTimeOffset now)
    {
        var trimmedTitle = title?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle))
        {
            return BookErrors.TitleRequired();
        }

        if (trimmedTitle.Length > TitleMaxLength)
        {
            return BookErrors.TitleTooLong();
        }

        var trimmedAuthor = author?.Trim();
        if (string.IsNullOrEmpty(trimmedAuthor))
        {
            return BookErrors.AuthorRequired();
        }

        if (trimmedAuthor.Length > AuthorMaxLength)
        {
            return BookErrors.AuthorTooLong();
        }

        var latestAllowedYear = now.Year;
        if (publishedYear < EarliestPublishedYear || publishedYear > latestAllowedYear)
        {
            return BookErrors.PublishedYearOutOfRange(latestAllowedYear);
        }

        // Version 7 GUIDs are time-ordered, so inserts land at the end of the primary key's
        // B-tree instead of scattering across it the way v4 does. The id is assigned here
        // rather than by the database so the aggregate is complete and valid before it ever
        // meets persistence.
        return Result<Book>.Success(
            new Book(Guid.CreateVersion7(), isbn, trimmedTitle, trimmedAuthor, publishedYear));
    }
}
