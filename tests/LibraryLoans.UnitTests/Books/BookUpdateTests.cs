using LibraryLoans.Domain.Books;

namespace LibraryLoans.UnitTests.Books;

/// <summary>
/// Correcting a catalogue entry, and the property that matters most about it: an update is held to
/// exactly the rules a create is.
///
/// These have to be unit tests, because they are unreachable over HTTP. The request DTO's
/// <c>[Required]</c> and <c>[StringLength]</c> attributes catch a blank or over-long title at the
/// boundary and answer 400, so an integration test can never drive the domain's own checks. Without
/// the theory below, deleting the shared validation from <see cref="Book.UpdateDetails"/> leaves
/// the entire suite green, and a 300-character title reaches a column declared for 200.
/// </summary>
public sealed class BookUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One case table, run against both entry points. If the two ever stop agreeing about what a
    /// valid title is, this fails rather than a reviewer noticing.
    /// </summary>
    public static TheoryData<string?, string?, int, string> InvalidDetails() => new()
    {
        { null, "An Author", 1937, "book.title.required" },
        { "   ", "An Author", 1937, "book.title.required" },
        { new string('t', Book.TitleMaxLength + 1), "An Author", 1937, "book.title.too_long" },
        { "A Title", null, 1937, "book.author.required" },
        { "A Title", "   ", 1937, "book.author.required" },
        { "A Title", new string('a', Book.AuthorMaxLength + 1), 1937, "book.author.too_long" },
        { "A Title", "An Author", Book.EarliestPublishedYear - 1, "book.published_year.out_of_range" },
        { "A Title", "An Author", 2027, "book.published_year.out_of_range" },
    };

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void Create_rejects_invalid_details(string? title, string? author, int year, string expectedCode)
    {
        var result = Book.Create(AnIsbn(), title, author, year, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void Update_rejects_exactly_what_create_rejects(string? title, string? author, int year, string expectedCode)
    {
        var book = AValidBook();

        var result = book.UpdateDetails(title, author, year, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Applies_the_new_details()
    {
        var book = AValidBook();

        var result = book.UpdateDetails("Nineteen Eighty-Four", "George Orwell", 1949, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nineteen Eighty-Four", book.Title);
        Assert.Equal("George Orwell", book.Author);
        Assert.Equal(1949, book.PublishedYear);
    }

    [Fact]
    public void Trims_the_new_details_as_create_does()
    {
        var book = AValidBook();

        book.UpdateDetails("  Spaced Title  ", "\tSpaced Author\n", 1949, Now);

        Assert.Equal("Spaced Title", book.Title);
        Assert.Equal("Spaced Author", book.Author);
    }

    /// <summary>
    /// A rejected update leaves the book exactly as it was. Validation happens before anything is
    /// assigned, so there is no partially-applied state to observe.
    /// </summary>
    [Fact]
    public void Leaves_the_book_untouched_when_it_rejects()
    {
        var book = AValidBook();
        var originalTitle = book.Title;
        var originalAuthor = book.Author;
        var originalYear = book.PublishedYear;

        Assert.False(book.UpdateDetails("A New Title", "   ", 1949, Now).IsSuccess);

        Assert.Equal(originalTitle, book.Title);
        Assert.Equal(originalAuthor, book.Author);
        Assert.Equal(originalYear, book.PublishedYear);
    }

    /// <summary>The ISBN identifies the work, and nothing on this type can change it.</summary>
    [Fact]
    public void Never_changes_the_isbn()
    {
        var book = AValidBook();
        var isbn = book.Isbn;

        book.UpdateDetails("Nineteen Eighty-Four", "George Orwell", 1949, Now);

        Assert.Equal(isbn, book.Isbn);
    }

    private static Isbn AnIsbn()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);
        return isbn.Value;
    }

    private static Book AValidBook()
    {
        var book = Book.Create(AnIsbn(), "The Hobbit", "J. R. R. Tolkien", 1937, Now);
        Assert.True(book.IsSuccess);
        return book.Value;
    }
}
