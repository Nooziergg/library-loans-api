using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Books;

/// <summary>
/// A catalogue title: the work, not a physical object. What a borrower actually carries home is
/// a physical copy of it, which is a separate concept: a library holds one Book and several
/// copies of it, and a loan is against a copy rather than against the title.
/// </summary>
public sealed class Book
{
    public const int TitleMaxLength = 200;
    public const int AuthorMaxLength = 150;

    /// <summary>
    /// Gutenberg's press, near enough. The point of a floor is not historical precision. It
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
        var details = ValidateDetails(title, author, publishedYear, now);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        // Version 7 GUIDs are time-ordered, so inserts land at the end of the primary key's
        // B-tree instead of scattering across it the way v4 does. The id is assigned here
        // rather than by the database so the aggregate is complete and valid before it ever
        // meets persistence.
        return Result<Book>.Success(new Book(
            Guid.CreateVersion7(),
            isbn,
            details.Value.Title,
            details.Value.Author,
            publishedYear));
    }

    /// <summary>
    /// Corrects a catalogue entry: a misspelled title, an author's name, a wrong year.
    ///
    /// <b>The ISBN is not among them, and cannot be.</b> It identifies the work; a row whose ISBN
    /// changed is describing a different book, and the honest shape of that operation is a delete
    /// and a create. The request type has no ISBN field at all rather than accepting one and
    /// rejecting a change: unrepresentable beats validated, which is the same argument this
    /// codebase makes for value objects in the first place.
    ///
    /// Shares its validation with <see cref="Create"/> rather than repeating it, so the two cannot
    /// drift into disagreeing about what a valid title is.
    /// </summary>
    public Result UpdateDetails(string? title, string? author, int publishedYear, DateTimeOffset now)
    {
        var details = ValidateDetails(title, author, publishedYear, now);
        if (!details.IsSuccess)
        {
            return details.Error;
        }

        Title = details.Value.Title;
        Author = details.Value.Author;
        PublishedYear = publishedYear;

        return Result.Success();
    }

    /// <summary>
    /// The rules about what a book's details may be, in one place, used by both the factory and the
    /// update. Returns the trimmed values so the caller does not have to remember to trim.
    /// </summary>
    private static Result<(string Title, string Author)> ValidateDetails(
        string? title,
        string? author,
        int publishedYear,
        DateTimeOffset now)
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

        return Result<(string, string)>.Success((trimmedTitle, trimmedAuthor));
    }
}
