using LibraryLoans.Domain.Books;

namespace LibraryLoans.UnitTests.Books;

/// <summary>
/// The ISBN value object is the system's smallest complete example of its validation strategy:
/// input is checked once, at construction, and an instance that exists is an instance that is
/// valid. Everything downstream gets to stop asking.
/// </summary>
public sealed class IsbnTests
{
    [Theory]
    // ISBN-13, plain and hyphenated.
    [InlineData("9780306406157", "9780306406157")]
    [InlineData("978-0-306-40615-7", "9780306406157")]
    [InlineData("978 0 306 40615 7", "9780306406157")]
    // ISBN-10 is accepted and re-encoded as its ISBN-13 equivalent.
    [InlineData("0306406152", "9780306406157")]
    [InlineData("0-306-40615-2", "9780306406157")]
    // Heavily separated but still within the input bound — the companion to
    // Rejects_over_long_input_even_when_it_would_otherwise_strip_to_a_valid_isbn below.
    [InlineData("9-7-8-0-3-0-6-4-0-6-1-5-7", "9780306406157")]
    // An ISBN-10 whose check digit is X — the case a naive digits-only parser rejects.
    [InlineData("043942089X", "9780439420891")]
    [InlineData("0-439-42089-X", "9780439420891")]
    [InlineData("043942089x", "9780439420891")]
    public void Accepts_valid_input_and_stores_the_canonical_form(string input, string expected)
    {
        var result = Isbn.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    /// <summary>
    /// The reason canonicalization exists rather than mere validation. Both of these are the
    /// same book, both satisfy their own checksum, and a type that stored them verbatim would
    /// let the catalogue hold it twice — under a unique index that claims otherwise.
    /// </summary>
    [Fact]
    public void An_isbn_10_and_its_isbn_13_equivalent_are_the_same_book()
    {
        var isbn10 = Isbn.Create("0306406152");
        var isbn13 = Isbn.Create("9780306406157");

        Assert.True(isbn10.IsSuccess);
        Assert.True(isbn13.IsSuccess);
        Assert.Equal(isbn13.Value, isbn10.Value);
        Assert.Equal(isbn13.Value.Value, isbn10.Value.Value);
    }

    [Theory]
    [InlineData("9780306406158")] // last digit altered
    [InlineData("9780306406150")]
    [InlineData("0306406153")] // ISBN-10 with a bad check digit
    [InlineData("0306406151")]
    public void Rejects_a_well_shaped_number_that_fails_its_check_digit(string input)
    {
        var result = Isbn.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.checksum_failed", result.Error.Code);
    }

    [Theory]
    [InlineData("123")] // too short
    [InlineData("97803064061570")] // too long
    [InlineData("978030640615")] // 12 digits: near-miss on ISBN-13
    [InlineData("97803064O157")] // letter O standing in for zero
    [InlineData("978-0-306-4061X-7")] // X somewhere it cannot appear
    [InlineData("X306406152")] // X in the first position of an ISBN-10
    public void Rejects_input_that_is_not_shaped_like_an_isbn(string input)
    {
        var result = Isbn.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.malformed", result.Error.Code);
    }

    /// <summary>
    /// Pins the length bound in <see cref="Isbn.Create"/>, which exists because parsing
    /// allocates a stack buffer sized from the input and the input arrives from a request body.
    /// A large enough value overflows the stack, and a StackOverflowException cannot be caught
    /// by any handler — the process dies. So an input-validation rule and a denial-of-service
    /// guard are the same line of code here.
    ///
    /// The input is chosen to fail *only* because of that bound. It is 37 characters of
    /// separators around digits that strip to a perfectly valid ISBN-13, so with the guard
    /// removed this input succeeds. A merely enormous string would not discriminate: it would
    /// be rejected anyway for having neither 10 nor 13 digits, and the test would pass while
    /// proving nothing.
    ///
    /// It also documents the non-obvious half of the rule — the bound is on the raw input,
    /// before separators are stripped, because that is what the allocation is sized from.
    /// </summary>
    [Fact]
    public void Rejects_over_long_input_even_when_it_would_otherwise_strip_to_a_valid_isbn()
    {
        const string paddedBeyondTheBound = "9--7--8--0--3--0--6--4--0--6--1--5--7";
        Assert.True(paddedBeyondTheBound.Length > Isbn.MaxInputLength);

        var result = Isbn.Create(paddedBeyondTheBound);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.malformed", result.Error.Code);
    }

    /// <summary>
    /// The rejected value is quoted back so the message is useful, but it came from the caller
    /// and travels into a response body and a log line, so it does not travel whole.
    /// </summary>
    [Fact]
    public void Does_not_echo_an_oversized_value_back_in_full()
    {
        var result = Isbn.Create(new string('9', 100_000));

        Assert.False(result.IsSuccess);
        Assert.True(
            result.Error.Message.Length < 200,
            $"The message should not carry the input back verbatim; it was {result.Error.Message.Length} characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_input(string? input)
    {
        var result = Isbn.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal("book.isbn.required", result.Error.Code);
    }

    /// <summary>
    /// Guards the ORM's materialization path against the canonical form drifting away from what
    /// <see cref="Isbn.Create"/> produces. If those two ever disagree, every row read back would
    /// differ from the row that was written.
    /// </summary>
    [Fact]
    public void Round_trips_through_the_persistence_constructor()
    {
        var created = Isbn.Create("978-0-306-40615-7");
        Assert.True(created.IsSuccess);

        var rehydrated = Isbn.FromPersistedValue(created.Value.Value);

        Assert.Equal(created.Value, rehydrated);
    }
}
