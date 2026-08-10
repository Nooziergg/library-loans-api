using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Common;
using LibraryLoans.Application.Loans;
using LibraryLoans.IntegrationTests.Infrastructure;

namespace LibraryLoans.IntegrationTests.Loans;

/// <summary>
/// Filtering the loan register by borrower, by whether the copy is still out, and by whether it is
/// late.
///
/// The overdue cases run against the seeded library, because an overdue loan needs a due date in the
/// past and the seed is the only place that exists without manipulating a clock mid-test.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoanSearchTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public LoanSearchTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Filters_by_borrower()
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        var bookId = await CreateBookAsync(client);
        var mine = await RegisterMemberAsync(client, "M00000001");
        var theirs = await RegisterMemberAsync(client, "M00000002");

        await BorrowAsync(client, mine, await AddCopyAsync(client, bookId, "COPY-0001"));
        await BorrowAsync(client, mine, await AddCopyAsync(client, bookId, "COPY-0002"));
        await BorrowAsync(client, theirs, await AddCopyAsync(client, bookId, "COPY-0003"));

        var page = await SearchAsync(client, $"?memberId={mine}");

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, loan => Assert.Equal(mine, loan.MemberId));
    }

    /// <summary>An unknown borrower is an empty page — this is a filter, not a subresource.</summary>
    [Fact]
    public async Task Reports_an_unknown_borrower_as_an_empty_page_rather_than_404()
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        var page = await SearchAsync(client, $"?memberId={Guid.CreateVersion7()}");

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Separates_loans_still_out_from_those_returned()
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        var bookId = await CreateBookAsync(client);
        var memberId = await RegisterMemberAsync(client, "M00000001");

        var returned = await BorrowAsync(client, memberId, await AddCopyAsync(client, bookId, "COPY-0001"));
        await BorrowAsync(client, memberId, await AddCopyAsync(client, bookId, "COPY-0002"));

        var response = await client.PostAsync($"/api/v1/loans/{returned}/return", null);
        response.EnsureSuccessStatusCode();

        Assert.Equal(1, (await SearchAsync(client, "?active=true")).TotalCount);
        Assert.Equal(1, (await SearchAsync(client, "?active=false")).TotalCount);
        Assert.Equal(2, (await SearchAsync(client, string.Empty)).TotalCount);
    }

    /// <summary>
    /// The rule behind this filter lives in the aggregate as an expression, so the database applies
    /// the same definition of "overdue" the domain would — rather than a copy of it written into a
    /// query.
    /// </summary>
    [Fact]
    public async Task Finds_the_overdue_loan_in_the_seeded_library()
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString, seed: true);
        using var client = factory.CreateClient();

        var overdue = await SearchAsync(client, "?overdue=true");
        var active = await SearchAsync(client, "?active=true&pageSize=1");

        Assert.True(overdue.TotalCount >= 1, "The seed is supposed to contain an overdue loan.");
        Assert.True(
            overdue.TotalCount < active.TotalCount,
            "Overdue should be a subset of active — a returned loan is never overdue.");

        Assert.All(overdue.Items, loan =>
        {
            Assert.Null(loan.ReturnedAt);
            Assert.True(loan.DueAt < DateTimeOffset.UtcNow);
        });
    }

    [Fact]
    public async Task Combines_the_borrower_and_overdue_filters()
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString, seed: true);
        using var client = factory.CreateClient();

        var overdue = await SearchAsync(client, "?overdue=true");
        var borrower = overdue.Items[0].MemberId;

        var page = await SearchAsync(client, $"?overdue=true&memberId={borrower}");

        Assert.All(page.Items, loan => Assert.Equal(borrower, loan.MemberId));
        Assert.True(page.TotalCount >= 1);
    }

    /// <summary>Both published sort fields have to translate, not just be accepted by the allowlist.</summary>
    [Theory]
    [InlineData("loanedAt")]
    [InlineData("dueAt")]
    public async Task Sorts_by_every_published_field(string sortBy)
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString, seed: true);
        using var client = factory.CreateClient();

        var ascending = await SearchAsync(client, $"?sortBy={sortBy}&pageSize=5");
        var descending = await SearchAsync(client, $"?sortBy={sortBy}&descending=true&pageSize=5");

        Assert.NotEmpty(ascending.Items);
        Assert.Equal(ascending.TotalCount, descending.TotalCount);
        Assert.NotEqual(ascending.Items[0].Id, descending.Items[0].Id);
    }

    [Theory]
    [InlineData("?sortBy=nonsense")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    // The upper page boundary: unbounded, (page - 1) * pageSize overflows int and PostgreSQL
    // rejects the resulting negative OFFSET with an error nothing translates.
    [InlineData("?page=999999999")]
    public async Task Rejects_a_malformed_query_with_400(string query)
    {
        await using var factory = new LibraryApiFactory(_postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/loans{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<PagedResponse<LoanResponse>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/loans{query}");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<LoanResponse>>();
        Assert.NotNull(page);

        return page;
    }

    private static async Task<Guid> CreateBookAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/books", new
        {
            isbn = "9780306406157",
            title = "The Hobbit",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<Guid> AddCopyAsync(HttpClient client, Guid bookId, string barcode)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/books/{bookId}/copies", new { barcode });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<Guid> RegisterMemberAsync(HttpClient client, string membershipNumber)
    {
        var response = await client.PostAsJsonAsync("/api/v1/members", new
        {
            membershipNumber,
            name = "A Borrower",
            email = "borrower@example.test",
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private static async Task<Guid> BorrowAsync(HttpClient client, Guid memberId, Guid bookCopyId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/loans", new { memberId, bookCopyId });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    private sealed record CreatedId(Guid Id);
}
