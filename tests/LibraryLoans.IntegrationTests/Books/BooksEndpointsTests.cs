using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Books;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.IntegrationTests.Books;

/// <summary>
/// The walking skeleton, exercised over real HTTP against a real database: request in at the
/// endpoint, out through the handler and the domain, into PostgreSQL, and back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BooksEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public BooksEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

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

    private static object AValidBook(string isbn = "978-0-306-40615-7") => new
    {
        isbn,
        title = "The Hobbit",
        author = "J. R. R. Tolkien",
        publishedYear = 1937,
    };

    [Fact]
    public async Task Creates_a_book_and_serves_it_back_from_the_location_header()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        var body = await created.Content.ReadFromJsonAsync<BookResponse>();
        Assert.NotNull(body);
        Assert.Equal("The Hobbit", body.Title);
        // Stored canonically, not as the hyphenated string that was sent.
        Assert.Equal("9780306406157", body.Isbn);

        var fetched = await _client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(body, await fetched.Content.ReadFromJsonAsync<BookResponse>());
    }

    [Fact]
    public async Task Reports_an_unknown_book_as_404()
    {
        var response = await _client.GetAsync($"/api/v1/books/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book.not_found", problem?.Extensions["code"]?.ToString());
    }

    /// <summary>
    /// A structurally impossible ISBN is not a malformed request — it is a well-formed request
    /// the domain refuses, which is what 422 is for. The 400 case below is the contrast.
    /// </summary>
    [Fact]
    public async Task Rejects_an_isbn_that_fails_its_check_digit_with_422()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/books", AValidBook("9780306406158"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book.isbn.checksum_failed", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Rejects_a_request_missing_required_fields_with_400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/books", new
        {
            isbn = "9780306406157",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_second_book_with_the_same_isbn_as_409()
    {
        var first = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book.isbn.duplicate", problem?.Extensions["code"]?.ToString());
    }

    /// <summary>
    /// An ISBN-10 and its ISBN-13 encoding are the same book, so the catalogue must refuse the
    /// second one. This is the test that would fail if the value object merely validated input
    /// instead of canonicalizing it — both forms pass their own checksum, and the unique index
    /// would happily hold two rows for one title.
    /// </summary>
    [Fact]
    public async Task Treats_an_isbn_10_and_its_isbn_13_form_as_the_same_book()
    {
        var asIsbn13 = await _client.PostAsJsonAsync("/api/v1/books", AValidBook("9780306406157"));
        Assert.Equal(HttpStatusCode.Created, asIsbn13.StatusCode);

        var asIsbn10 = await _client.PostAsJsonAsync("/api/v1/books", AValidBook("0306406152"));

        Assert.Equal(HttpStatusCode.Conflict, asIsbn10.StatusCode);
    }

    /// <summary>
    /// The reason the uniqueness rule is enforced twice.
    ///
    /// Both requests can pass the handler's "does this ISBN exist yet" check microseconds apart,
    /// because at that moment it is true for both. Only the unique index can decide which one
    /// wins. Without translating its violation into a 409, the loser would receive a 500 — a
    /// server fault reported for what is really an ordinary, expected outcome.
    ///
    /// The same mechanism is what a higher-stakes invariant needs — a physical copy on two active
    /// loans at once is the version of this bug that a library cannot honour.
    /// </summary>
    [Fact]
    public async Task Allows_exactly_one_of_two_simultaneous_creates_of_the_same_isbn()
    {
        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/v1/books", AValidBook()),
            _client.PostAsJsonAsync("/api/v1/books", AValidBook()));

        var statuses = responses.Select(response => response.StatusCode).ToArray();

        Assert.Single(statuses, HttpStatusCode.Created);
        Assert.Single(statuses, HttpStatusCode.Conflict);
    }
}
