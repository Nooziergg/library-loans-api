using LibraryLoans.Domain.Copies;

namespace LibraryLoans.UnitTests.Copies;

public sealed class BarcodeTests
{
    [Theory]
    [InlineData("COPY-0001", "COPY-0001")]
    [InlineData("copy-0001", "COPY-0001")]
    [InlineData("  copy-0001  ", "COPY-0001")]
    [InlineData("ABC123", "ABC123")]
    public void Accepts_and_canonicalises_a_barcode(string input, string expected)
    {
        var result = Barcode.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    /// <summary>
    /// Case is not part of a barcode's identity, so two spellings of one label must not become two
    /// rows under a unique index claiming they cannot.
    /// </summary>
    [Fact]
    public void Treats_differently_cased_spellings_as_the_same_barcode()
    {
        var lower = Barcode.Create("copy-0001");
        var upper = Barcode.Create("COPY-0001");

        Assert.True(lower.IsSuccess);
        Assert.True(upper.IsSuccess);
        Assert.Equal(upper.Value, lower.Value);
    }

    /// <summary>
    /// The deliberate asymmetry with ISBN. A hyphen in an ISBN is conventional grouping and carries
    /// no information, so it is stripped. A barcode's characters are the printed label, so removing
    /// one would merge two labels a librarian can hold up and tell apart.
    /// </summary>
    [Fact]
    public void Does_not_treat_a_hyphen_as_noise_the_way_an_isbn_does()
    {
        var hyphenated = Barcode.Create("ABC-1");
        var plain = Barcode.Create("ABC1");

        Assert.True(hyphenated.IsSuccess);
        Assert.True(plain.IsSuccess);
        Assert.NotEqual(plain.Value, hyphenated.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_input(string? input)
    {
        var result = Barcode.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("book_copy.barcode.required", result.Error.Code);
    }

    [Theory]
    [InlineData("COPY 0001")]
    [InlineData("COPY_0001")]
    [InlineData("COPY/0001")]
    [InlineData("COPY#1")]
    public void Rejects_characters_that_cannot_appear_on_a_label(string input)
    {
        var result = Barcode.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("book_copy.barcode.malformed", result.Error.Code);
    }

    /// <summary>
    /// The bound exists before any allocation sized from the input, for the same reason it does on
    /// ISBN. It also has to match the column width exactly, or an accepted value would be rejected
    /// by PostgreSQL as SQLSTATE 22001, which nothing translates, so a caller-controlled string
    /// would produce a 500.
    /// </summary>
    [Fact]
    public void Rejects_a_barcode_longer_than_the_column_allows()
    {
        var result = Barcode.Create(new string('A', Barcode.MaxLength + 1));

        Assert.False(result.IsSuccess);
        Assert.Equal("book_copy.barcode.too_long", result.Error.Code);
    }

    [Fact]
    public void Accepts_a_barcode_exactly_at_the_limit()
    {
        var result = Barcode.Create(new string('A', Barcode.MaxLength));

        Assert.True(result.IsSuccess);
        Assert.Equal(Barcode.MaxLength, result.Value.Value.Length);
    }

    [Fact]
    public void Round_trips_through_the_persistence_constructor()
    {
        var created = Barcode.Create("copy-0001");
        Assert.True(created.IsSuccess);

        Assert.Equal(created.Value, Barcode.FromPersistedValue(created.Value.Value));
    }
}
