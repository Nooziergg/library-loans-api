using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;
using LibraryLoans.IntegrationTests.Infrastructure;

namespace LibraryLoans.IntegrationTests.Books;

/// <summary>
/// Searching, filtering, sorting and paging the catalogue — the brief's own words, over real HTTP
/// against real PostgreSQL.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BookSearchTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public BookSearchTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        await _postgres.ResetAsync();
        _factory = new LibraryApiFactory(_postgres.ConnectionString);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ── Search ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Finds_a_book_by_part_of_its_title()
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var page = await SearchAsync("?search=eighty");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Nineteen Eighty-Four", page.Items[0].Title);
    }

    [Fact]
    public async Task Finds_a_book_by_part_of_its_author_case_insensitively()
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var page = await SearchAsync("?search=ORWELL");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("George Orwell", page.Items[0].Author);
    }

    /// <summary>
    /// The ISBN a caller types is the one printed on the book — hyphenated, and possibly the older
    /// ten-digit form. Stored ISBNs are canonical thirteen-digit strings, so matching the term as
    /// raw text would find nothing at all. Running it through the value object first is what makes
    /// every spelling of one ISBN find the same row.
    /// </summary>
    [Theory]
    [InlineData("9780306406157")]      // canonical, as stored
    [InlineData("978-0-306-40615-7")]  // as printed
    [InlineData("0306406152")]         // the ISBN-10 of the same book
    [InlineData("0-306-40615-2")]      // and hyphenated
    public async Task Finds_a_book_by_any_spelling_of_its_isbn(string searchTerm)
    {
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);

        var page = await SearchAsync($"?search={Uri.EscapeDataString(searchTerm)}");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("9780306406157", page.Items[0].Isbn);
    }

    /// <summary>
    /// <c>%</c> and <c>_</c> are LIKE wildcards. Unescaped, a search for <c>%</c> would return the
    /// entire catalogue, and a term alternating wildcards with literals is a cheap way to make
    /// pattern matching expensive on an endpoint that is deliberately unauthenticated.
    /// </summary>
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("%a%b%c%")]
    public async Task Treats_like_wildcards_in_the_search_term_as_literal_characters(string term)
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var page = await SearchAsync($"?search={Uri.EscapeDataString(term)}");

        // No title or author here contains these characters, so a correct implementation matches
        // nothing. A broken one matches everything.
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Returns_every_book_when_no_search_term_is_given()
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var page = await SearchAsync(string.Empty);

        Assert.Equal(2, page.TotalCount);
    }

    // ── availableOnly ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The filter is a correlated anti-join over loans, served by the same partial unique index
    /// that enforces the loan invariant — which is why a copy carries no availability column that
    /// could disagree with the loans table.
    /// </summary>
    [Fact]
    public async Task Excludes_a_title_whose_only_copy_is_out_and_includes_it_again_once_returned()
    {
        var bookId = await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);
        var copyId = await AddCopyAsync(bookId, "COPY-0001");
        var memberId = await RegisterMemberAsync("M00000001");

        Assert.Equal(1, (await SearchAsync("?availableOnly=true")).TotalCount);

        var loan = await BorrowAsync(memberId, copyId);
        Assert.Equal(0, (await SearchAsync("?availableOnly=true")).TotalCount);
        // Still in the catalogue — it is unavailable, not absent.
        Assert.Equal(1, (await SearchAsync(string.Empty)).TotalCount);

        await _client.PostAsync($"/api/v1/loans/{loan}/return", null);

        Assert.Equal(1, (await SearchAsync("?availableOnly=true")).TotalCount);
    }

    [Fact]
    public async Task Includes_a_title_with_one_copy_out_and_another_free()
    {
        var bookId = await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);
        var borrowed = await AddCopyAsync(bookId, "COPY-0001");
        await AddCopyAsync(bookId, "COPY-0002");
        var memberId = await RegisterMemberAsync("M00000001");

        await BorrowAsync(memberId, borrowed);

        Assert.Equal(1, (await SearchAsync("?availableOnly=true")).TotalCount);
    }

    [Fact]
    public async Task Excludes_a_title_that_has_no_copies_at_all()
    {
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        Assert.Equal(0, (await SearchAsync("?availableOnly=true")).TotalCount);
    }

    // ── Paging and sorting ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pages_through_results_and_reports_the_total()
    {
        await CreateManyAsync(12);

        var first = await SearchAsync("?page=1&pageSize=5&sortBy=title");
        var last = await SearchAsync("?page=3&pageSize=5&sortBy=title");

        Assert.Equal(12, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.True(first.HasNextPage);
        Assert.Equal(5, first.Items.Count);

        Assert.Equal(2, last.Items.Count);
        Assert.False(last.HasNextPage);
    }

    /// <summary>
    /// Every value the allowlist publishes has to actually translate. <c>isbn</c> is the one worth
    /// singling out: it orders by a property behind a value converter, which is the combination most
    /// likely to behave differently from the plain columns the other cases cover. It is published in
    /// the OpenAPI document, so a reviewer can type it.
    /// </summary>
    [Theory]
    [InlineData("title")]
    [InlineData("author")]
    [InlineData("publishedYear")]
    [InlineData("isbn")]
    public async Task Sorts_by_every_published_field(string sortBy)
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var ascending = await SearchAsync($"?sortBy={sortBy}");
        var descending = await SearchAsync($"?sortBy={sortBy}&descending=true");

        Assert.Equal(2, ascending.TotalCount);
        Assert.Equal(ascending.Items[0].Id, descending.Items[1].Id);
    }

    [Fact]
    public async Task Sorts_in_both_directions()
    {
        await CreateBookAsync("9780451524935", "Nineteen Eighty-Four", "George Orwell", 1949);
        await CreateBookAsync("9780306406157", "The Hobbit", "J. R. R. Tolkien", 1937);

        var ascending = await SearchAsync("?sortBy=publishedYear");
        var descending = await SearchAsync("?sortBy=publishedYear&descending=true");

        Assert.Equal(1937, ascending.Items[0].PublishedYear);
        Assert.Equal(1949, descending.Items[0].PublishedYear);
    }

    /// <summary>
    /// The test the tiebreaker exists for.
    ///
    /// Sorting by a column with duplicate values leaves ties in an order PostgreSQL does not
    /// define. With LIMIT/OFFSET that means a row can appear on two pages, or on none — and every
    /// book here shares an author deliberately, so the sort key is ties all the way down. Without
    /// `.ThenBy(id)` this fails; with it, paging is a partition of the whole set.
    /// </summary>
    [Fact]
    public async Task Pages_form_a_partition_of_the_catalogue_when_the_sort_key_is_all_ties()
    {
        const int total = 15;
        const int pageSize = 4;
        await CreateManyAsync(total, sharedAuthor: "One Author");

        var seen = new List<Guid>();
        for (var page = 1; page <= (total + pageSize - 1) / pageSize; page++)
        {
            var results = await SearchAsync($"?page={page}&pageSize={pageSize}&sortBy=author");
            seen.AddRange(results.Items.Select(book => book.Id));
        }

        Assert.Equal(total, seen.Count);
        Assert.Equal(total, seen.Distinct().Count());
    }

    // ── Shape errors are 400, not 422 ─────────────────────────────────────────────────────────

    /// <summary>
    /// A sort field nobody publishes is a malformed request, not a domain refusal. It is rejected
    /// by the allowlist on the request DTO before the application sees it, which is what makes it a
    /// 400 — and what keeps a caller-supplied string out of the query builder entirely.
    /// </summary>
    [Theory]
    [InlineData("?sortBy=nonsense")]
    [InlineData("?sortBy=1;DROP TABLE books")]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=100000")]
    // The upper boundary. Unbounded, (page - 1) * pageSize overflows int, wraps negative, and
    // PostgreSQL rejects the negative OFFSET with an error nothing translates — a 500 for a
    // value the API itself declared valid.
    [InlineData("?page=999999999")]
    [InlineData("?page=2147483647")]
    public async Task Rejects_a_malformed_query_with_400(string query)
    {
        var response = await _client.GetAsync($"/api/v1/books{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Accepts_the_largest_permitted_page_size()
    {
        var response = await _client.GetAsync("/api/v1/books?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Arrangement ──────────────────────────────────────────────────────────────────────────

    private async Task<PagedResponse<BookResponse>> SearchAsync(string query)
    {
        var response = await _client.GetAsync($"/api/v1/books{query}");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<BookResponse>>();
        Assert.NotNull(page);

        return page;
    }

    private async Task CreateManyAsync(int count, string? sharedAuthor = null)
    {
        for (var index = 0; index < count; index++)
        {
            await CreateBookAsync(
                IsbnFor(index),
                $"Title {index:D2}",
                sharedAuthor ?? $"Author {index:D2}",
                1900 + index);
        }
    }

    /// <summary>
    /// Builds a valid ISBN-13 with a correct check digit, because the domain refuses anything else
    /// and a test that needs twelve books should not need twelve hand-picked real ones.
    /// </summary>
    private static string IsbnFor(int index)
    {
        var body = $"978{index:D9}";
        var sum = 0;
        for (var position = 0; position < body.Length; position++)
        {
            sum += (body[position] - '0') * (position % 2 == 0 ? 1 : 3);
        }

        return body + (10 - (sum % 10)) % 10;
    }

    private async Task<Guid> CreateBookAsync(string isbn, string title, string author, int publishedYear)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/books", new { isbn, title, author, publishedYear });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> AddCopyAsync(Guid bookId, string barcode)
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> RegisterMemberAsync(string membershipNumber)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/members", new
        {
            membershipNumber,
            name = "A Borrower",
            email = "borrower@example.test",
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> BorrowAsync(Guid memberId, Guid bookCopyId)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/loans", new { memberId, bookCopyId });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private sealed record CreatedId(Guid Id);
}
