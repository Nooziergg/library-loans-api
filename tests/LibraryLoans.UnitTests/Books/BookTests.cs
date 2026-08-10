using LibraryLoans.Domain.Books;

namespace LibraryLoans.UnitTests.Books;

public sealed class BookTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static Isbn AnIsbn()
    {
        var isbn = Isbn.Create("9780306406157");
        Assert.True(isbn.IsSuccess);
        return isbn.Value;
    }

    [Fact]
    public void Creates_a_book_from_valid_details()
    {
        var result = Book.Create(AnIsbn(), "The Hobbit", "J. R. R. Tolkien", 1937, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("The Hobbit", result.Value.Title);
        Assert.Equal("J. R. R. Tolkien", result.Value.Author);
        Assert.Equal(1937, result.Value.PublishedYear);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public void Trims_surrounding_whitespace()
    {
        var result = Book.Create(AnIsbn(), "  The Hobbit  ", "\tJ. R. R. Tolkien\n", 1937, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("The Hobbit", result.Value.Title);
        Assert.Equal("J. R. R. Tolkien", result.Value.Author);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_title(string? title)
    {
        var result = Book.Create(AnIsbn(), title, "J. R. R. Tolkien", 1937, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.title.required", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_an_author(string? author)
    {
        var result = Book.Create(AnIsbn(), "The Hobbit", author, 1937, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.author.required", result.Error.Code);
    }

    [Fact]
    public void Rejects_a_title_longer_than_the_column_allows()
    {
        var result = Book.Create(
            AnIsbn(),
            new string('t', Book.TitleMaxLength + 1),
            "J. R. R. Tolkien",
            1937,
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.title.too_long", result.Error.Code);
    }

    [Fact]
    public void Rejects_an_author_longer_than_the_column_allows()
    {
        var result = Book.Create(
            AnIsbn(),
            "The Hobbit",
            new string('a', Book.AuthorMaxLength + 1),
            1937,
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.author.too_long", result.Error.Code);
    }

    /// <summary>
    /// The clock is a parameter, so this is a plain assertion rather than a test that would
    /// start failing on 1 January. That is the entire argument for injecting
    /// <c>TimeProvider</c> instead of reading <c>DateTime.UtcNow</c> inside the aggregate.
    /// </summary>
    [Fact]
    public void Rejects_a_book_published_in_the_future()
    {
        var result = Book.Create(AnIsbn(), "The Hobbit", "J. R. R. Tolkien", Now.Year + 1, Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.published_year.out_of_range", result.Error.Code);
    }

    [Fact]
    public void Accepts_a_book_published_this_year()
    {
        var result = Book.Create(AnIsbn(), "Something Recent", "An Author", Now.Year, Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Rejects_a_year_before_printing_existed()
    {
        var result = Book.Create(
            AnIsbn(),
            "The Hobbit",
            "J. R. R. Tolkien",
            Book.EarliestPublishedYear - 1,
            Now);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.published_year.out_of_range", result.Error.Code);
    }

    /// <summary>
    /// The message has to carry both bounds, because a client that is told only "out of range"
    /// cannot correct the request without guessing.
    /// </summary>
    [Fact]
    public void Says_what_the_allowed_year_range_is()
    {
        var result = Book.Create(AnIsbn(), "The Hobbit", "J. R. R. Tolkien", 3000, Now);

        Assert.False(result.IsSuccess);
        Assert.Contains(Book.EarliestPublishedYear.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(Now.Year.ToString(), result.Error.Message, StringComparison.Ordinal);
    }
}
