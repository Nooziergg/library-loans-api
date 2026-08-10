using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Common;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Members;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.IntegrationTests.Members;

/// <summary>
/// Reading and suspending borrowers, and listing the copies of a title — the endpoints that close
/// resources which could previously be created and never read.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MembersEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public MembersEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

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
    public async Task Reads_a_registered_borrower_back()
    {
        var id = await RegisterAsync("M00000001", "Alice Whitfield", "alice@example.test");

        var response = await _client.GetAsync($"/api/v1/members/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<MemberResponse>();
        Assert.NotNull(member);
        Assert.Equal("M00000001", member.MembershipNumber);
        Assert.Equal("Alice Whitfield", member.Name);
        Assert.Equal("Active", member.Status);
    }

    [Fact]
    public async Task Reports_an_unknown_borrower_as_404()
    {
        var response = await _client.GetAsync($"/api/v1/members/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("member.not_found", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Suspends_a_borrower_and_then_refuses_their_borrowing()
    {
        var bookId = await CreateBookAsync();
        var copyId = await AddCopyAsync(bookId, "COPY-0001");
        var memberId = await RegisterAsync("M00000001", "Alice Whitfield", "alice@example.test");

        var suspended = await _client.PostAsync($"/api/v1/members/{memberId}/suspend", null);

        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("Suspended", (await suspended.Content.ReadFromJsonAsync<MemberResponse>())!.Status);

        var borrow = await _client.PostAsJsonAsync("/api/v1/loans", new { memberId, bookCopyId = copyId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, borrow.StatusCode);
        var problem = await borrow.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.member.suspended", problem?.Extensions["code"]?.ToString());
    }

    /// <summary>Consistent with refusing a second return: an operation that quietly does nothing lies to its caller.</summary>
    [Fact]
    public async Task Refuses_to_suspend_a_borrower_twice()
    {
        var memberId = await RegisterAsync("M00000001", "Alice Whitfield", "alice@example.test");

        await _client.PostAsync($"/api/v1/members/{memberId}/suspend", null);
        var second = await _client.PostAsync($"/api/v1/members/{memberId}/suspend", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("member.already_suspended", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Filters_the_register_by_status()
    {
        await RegisterAsync("M00000001", "Alice Whitfield", "alice@example.test");
        var suspendedId = await RegisterAsync("M00000002", "Bruno Castellani", "bruno@example.test");
        await _client.PostAsync($"/api/v1/members/{suspendedId}/suspend", null);

        var all = await SearchMembersAsync(string.Empty);
        var active = await SearchMembersAsync("?status=Active");
        var suspended = await SearchMembersAsync("?status=Suspended");

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(1, active.TotalCount);
        Assert.Equal(1, suspended.TotalCount);
        Assert.Equal("Bruno Castellani", suspended.Items[0].Name);
    }

    [Fact]
    public async Task Rejects_an_unknown_status_with_400()
    {
        var response = await _client.GetAsync("/api/v1/members?status=Lapsed");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Copies of a title ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lists_the_copies_of_a_title()
    {
        var bookId = await CreateBookAsync();
        await AddCopyAsync(bookId, "COPY-0001");
        await AddCopyAsync(bookId, "COPY-0002");

        var response = await _client.GetAsync($"/api/v1/books/{bookId}/copies");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<BookCopyResponse>>();
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, copy => Assert.Equal(bookId, copy.BookId));
    }

    /// <summary>
    /// A subresource, so an unknown title is a 404 — the question "which copies does it have" has no
    /// answer if the title does not exist. Contrast the loan filters, where an unknown member id
    /// correctly returns an empty page.
    /// </summary>
    [Fact]
    public async Task Reports_copies_of_an_unknown_title_as_404_not_an_empty_page()
    {
        var response = await _client.GetAsync($"/api/v1/books/{Guid.CreateVersion7()}/copies");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("book.not_found", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Returns_an_empty_page_for_a_title_with_no_copies()
    {
        var bookId = await CreateBookAsync();

        var response = await _client.GetAsync($"/api/v1/books/{bookId}/copies");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<BookCopyResponse>>();
        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
    }

    private async Task<PagedResponse<MemberResponse>> SearchMembersAsync(string query)
    {
        var response = await _client.GetAsync($"/api/v1/members{query}");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<MemberResponse>>();
        Assert.NotNull(page);

        return page;
    }

    private async Task<Guid> RegisterAsync(string membershipNumber, string name, string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/members", new { membershipNumber, name, email });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
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

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private async Task<Guid> AddCopyAsync(Guid bookId, string barcode)
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private sealed record CreatedId(Guid Id);
}
