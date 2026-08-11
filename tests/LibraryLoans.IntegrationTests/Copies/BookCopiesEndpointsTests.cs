using System.Net;
using System.Net.Http.Json;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.IntegrationTests.Copies;

/// <summary>
/// Adding physical copies of a title.
///
/// The unknown-book case is the one worth having. <c>AddBookCopyHandler</c> loads the book before
/// constructing the copy, and that guard is the only thing standing between an invented
/// <c>bookId</c> and a foreign-key violation: SQLSTATE 23503, which is not a unique violation, so
/// nothing translates it and the unit of work rethrows. Remove the guard and this endpoint answers
/// 500 for a mistake a caller can make by mistyping a URL.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BookCopiesEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public BookCopiesEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

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

    [Fact]
    public async Task Adds_a_copy_of_an_existing_title()
    {
        var bookId = await CreateBookAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/books/{bookId}/copies",
            new { barcode = "copy-0001" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CopyBody>();
        Assert.NotNull(body);
        Assert.Equal(bookId, body.BookId);
        // Canonicalised: a barcode's case is not part of its identity.
        Assert.Equal("COPY-0001", body.Barcode);

        // No Location header, deliberately. There is no read endpoint for a copy yet, and a header
        // pointing at a 404 would be worse than none.
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Reports_an_unknown_title_as_404_rather_than_letting_the_foreign_key_decide()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/books/{Guid.CreateVersion7()}/copies",
            new { barcode = "COPY-0001" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book.not_found", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Rejects_a_duplicate_barcode_as_409()
    {
        var bookId = await CreateBookAsync();
        var barcode = new { barcode = "COPY-0001" };

        Assert.Equal(
            HttpStatusCode.Created,
            (await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", barcode)).StatusCode);

        var second = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", barcode);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book_copy.barcode.duplicate", problem?.Extensions["code"]?.ToString());
    }

    /// <summary>Differently-cased spellings of one label are one barcode, so the second is a conflict.</summary>
    [Fact]
    public async Task Treats_a_differently_cased_barcode_as_the_same_copy()
    {
        var bookId = await CreateBookAsync();

        Assert.Equal(
            HttpStatusCode.Created,
            (await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode = "COPY-0001" })).StatusCode);

        var second = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode = "copy-0001" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_malformed_barcode_with_422()
    {
        var bookId = await CreateBookAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/books/{bookId}/copies",
            new { barcode = "COPY/0001" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book_copy.barcode.malformed", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Rejects_a_request_with_no_barcode_with_400()
    {
        var bookId = await CreateBookAsync();

        var response = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreateBookAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/books", new
        {
            isbn = "9780306406157",
            title = "The Hobbit",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CopyBody>();
        return body!.Id;
    }

    private sealed record CopyBody(Guid Id, Guid BookId, string Barcode);
}
