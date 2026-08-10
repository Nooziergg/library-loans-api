using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Books;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.IntegrationTests.Books;

/// <summary>
/// Updating and deleting catalogue entries — the U and D of CRUD.
///
/// The delete cases are the interesting ones, and the second is the one that would have been a 500.
/// The foreign key from loans to copies is <c>Restrict</c>, so a loan blocks the delete whether or
/// not the copy has come back: a precondition checking only for <i>active</i> loans would pass, the
/// database would reject the statement with a foreign-key violation, and nothing would translate
/// it. Hence two preconditions with two distinct codes — one retryable, one not.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BookLifecycleTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public BookLifecycleTests(PostgresFixture postgres) => _postgres = postgres;

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

    // ── Update ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Corrects_a_titles_details()
    {
        var id = await CreateBookAsync();

        var response = await _client.PutAsJsonAsync($"/api/v1/books/{id}", new
        {
            title = "The Hobbit, or There and Back Again",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<BookResponse>();
        Assert.Equal("The Hobbit, or There and Back Again", book!.Title);

        var refetched = await _client.GetFromJsonAsync<BookResponse>($"/api/v1/books/{id}");
        Assert.Equal("The Hobbit, or There and Back Again", refetched!.Title);
        // Untouched, and untouchable: the request has no field for it.
        Assert.Equal("9780306406157", refetched.Isbn);
    }

    /// <summary>
    /// Update is held to the same rules as create, and rejects along the same two-layer split.
    ///
    /// A blank title is a **400**: it is a shape error, caught by the request DTO before a handler
    /// runs, exactly as it is on create. A year in the future is a **422**: the bound depends on the
    /// current time, so it cannot be an attribute and the domain is the only thing that can decide
    /// it. That asymmetry is not an accident of where the checks happen — it is what the two layers
    /// are for, and it is why the domain's own title check is not dead code even though HTTP callers
    /// never reach it.
    /// </summary>
    [Fact]
    public async Task Applies_the_same_rules_to_an_update_as_to_a_create()
    {
        var id = await CreateBookAsync();

        var blankTitle = await _client.PutAsJsonAsync($"/api/v1/books/{id}", new
        {
            title = "   ",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });
        Assert.Equal(HttpStatusCode.BadRequest, blankTitle.StatusCode);

        var future = await _client.PutAsJsonAsync($"/api/v1/books/{id}", new
        {
            title = "The Hobbit",
            author = "J. R. R. Tolkien",
            publishedYear = DateTimeOffset.UtcNow.Year + 1,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, future.StatusCode);
        Assert.Equal(
            "book.published_year.out_of_range",
            (await future.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Reports_an_update_to_an_unknown_title_as_404()
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/books/{Guid.CreateVersion7()}", new
        {
            title = "The Hobbit",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Delete ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deletes_a_title_and_its_copies_when_it_has_never_been_borrowed()
    {
        var id = await CreateBookAsync();
        await AddCopyAsync(id, "COPY-0001");
        await AddCopyAsync(id, "COPY-0002");

        var response = await _client.DeleteAsync($"/api/v1/books/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/books/{id}")).StatusCode);
        // The copies went with it, and the copy listing now 404s because the title is gone.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/books/{id}/copies")).StatusCode);
    }

    [Fact]
    public async Task Refuses_to_delete_a_title_with_a_copy_currently_out()
    {
        var id = await CreateBookAsync();
        var copyId = await AddCopyAsync(id, "COPY-0001");
        var memberId = await RegisterMemberAsync();
        await BorrowAsync(memberId, copyId);

        var response = await _client.DeleteAsync($"/api/v1/books/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "book.copy_on_loan",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString());
    }

    /// <summary>
    /// The case that would have been a 500. The copy is back on the shelf, so a precondition looking
    /// only for active loans would let the delete through — and the foreign key from the returned
    /// loan to the copy would reject it as an untranslated violation.
    ///
    /// It is also a genuinely different answer for a caller: unlike the case above, waiting will
    /// never help. Hence a distinct code.
    /// </summary>
    [Fact]
    public async Task Refuses_to_delete_a_title_that_has_been_borrowed_and_returned()
    {
        var id = await CreateBookAsync();
        var copyId = await AddCopyAsync(id, "COPY-0001");
        var memberId = await RegisterMemberAsync();
        var loanId = await BorrowAsync(memberId, copyId);

        var returned = await _client.PostAsync($"/api/v1/loans/{loanId}/return", null);
        returned.EnsureSuccessStatusCode();

        var response = await _client.DeleteAsync($"/api/v1/books/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "book.has_loan_history",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString());

        // Still there, and still readable — refused, not partially applied.
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/v1/books/{id}")).StatusCode);
    }

    [Fact]
    public async Task Reports_a_delete_of_an_unknown_title_as_404()
    {
        var response = await _client.DeleteAsync($"/api/v1/books/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Frees_the_isbn_for_reuse_once_the_title_is_deleted()
    {
        var id = await CreateBookAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/books/{id}")).StatusCode);

        // A soft delete would fail here: the row would keep its ISBN and the unique index would
        // reject this forever. One of the reasons deletion is real rather than a flag.
        var recreated = await _client.PostAsJsonAsync("/api/v1/books", Book());

        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
    }

    private static object Book() => new
    {
        isbn = "9780306406157",
        title = "The Hobbit",
        author = "J. R. R. Tolkien",
        publishedYear = 1937,
    };

    private async Task<Guid> CreateBookAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/books", Book());
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> AddCopyAsync(Guid bookId, string barcode)
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> RegisterMemberAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/members", new
        {
            membershipNumber = "M00000001",
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
