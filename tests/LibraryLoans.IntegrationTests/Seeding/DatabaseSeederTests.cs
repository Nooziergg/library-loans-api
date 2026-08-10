using System.Net.Http.Json;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;
using LibraryLoans.Infrastructure.Persistence;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.IntegrationTests.Seeding;

/// <summary>
/// The seed exists so a reviewer with only Docker can watch the rules fire rather than read that
/// they exist. These tests assert that it produces enough data to satisfy the brief, that it puts
/// the interesting states in it, and — the one that matters operationally — that running it twice
/// does not duplicate anything.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseSeederTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public DatabaseSeederTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeds_more_rows_than_the_brief_asks_for()
    {
        await using var factory = SeededFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var books = await dbContext.Books.CountAsync();
        var copies = await dbContext.BookCopies.CountAsync();
        var members = await dbContext.Members.CountAsync();
        var loans = await dbContext.Loans.CountAsync();

        Assert.True(
            books + copies + members + loans >= 100,
            $"The brief asks for at least 100 rows; got {books + copies + members + loans}.");
        Assert.True(books >= 50, $"Expected a catalogue worth searching; got {books} books.");
    }

    /// <summary>
    /// The test that protects a reviewer's database. A seeder that is not idempotent duplicates
    /// everything on <c>docker compose restart</c> — and because every natural key here is unique,
    /// the second run would not merely duplicate but fail partway, leaving a mess with no
    /// indication of what happened.
    /// </summary>
    [Fact]
    public async Task Running_twice_against_one_database_changes_nothing()
    {
        int booksAfterFirstRun;
        int loansAfterFirstRun;

        await using (var first = SeededFactory())
        {
            await using var scope = first.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            booksAfterFirstRun = await dbContext.Books.CountAsync();
            loansAfterFirstRun = await dbContext.Loans.CountAsync();
        }

        await using var second = SeededFactory();
        await using var secondScope = second.Services.CreateAsyncScope();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        Assert.Equal(booksAfterFirstRun, await secondContext.Books.CountAsync());
        Assert.Equal(loansAfterFirstRun, await secondContext.Loans.CountAsync());
    }

    /// <summary>
    /// Every seeded row went through the domain factories, so this is really asserting that sixty
    /// titles, a hundred and fifty copies and eighty loans all satisfied the invariants on the way
    /// in — including the one that says a copy cannot be on two active loans at once.
    /// </summary>
    [Fact]
    public async Task No_copy_is_on_two_active_loans()
    {
        await using var factory = SeededFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var copiesWithMultipleActiveLoans = await dbContext.Loans
            .Where(loan => loan.ReturnedAt == null)
            .GroupBy(loan => loan.BookCopyId)
            .CountAsync(group => group.Count() > 1);

        Assert.Equal(0, copiesWithMultipleActiveLoans);
    }

    [Fact]
    public async Task Includes_the_states_that_make_the_rules_visible()
    {
        await using var factory = SeededFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var now = DateTimeOffset.UtcNow;

        Assert.True(
            await dbContext.Loans.AnyAsync(loan => loan.ReturnedAt == null && loan.DueAt < now),
            "Expected an overdue loan, so ?overdue=true returns something.");

        Assert.True(
            await dbContext.Members.AnyAsync(member => member.Status == Domain.Members.MemberStatus.Suspended),
            "Expected a suspended member, so borrowing can be seen to be refused.");

        Assert.True(
            await dbContext.Loans.AnyAsync(loan => loan.ReturnedAt != null),
            "Expected returned loans, so a copy can be seen to be borrowable again.");

        var loansPerMember = await dbContext.Loans
            .Where(loan => loan.ReturnedAt == null)
            .GroupBy(loan => loan.MemberId)
            .Select(group => group.Count())
            .ToListAsync();

        Assert.Contains(5, loansPerMember);
        Assert.DoesNotContain(loansPerMember, count => count > 5);
    }

    /// <summary>
    /// The seed's payoff, seen the way a reviewer sees it: a title whose every copy is out is still
    /// in the catalogue and absent from the available list.
    /// </summary>
    [Fact]
    public async Task A_fully_borrowed_title_is_searchable_but_not_available()
    {
        await using var factory = SeededFactory();
        using var client = factory.CreateClient();

        var all = await SearchAsync(client, "?pageSize=1");
        var available = await SearchAsync(client, "?availableOnly=true&pageSize=1");

        Assert.True(all.TotalCount > available.TotalCount,
            "Expected at least one title to have every copy on loan.");
    }

    private LibraryApiFactory SeededFactory() => new(_postgres.ConnectionString, seed: true);

    private static async Task<PagedResponse<BookResponse>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/books{query}");
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<BookResponse>>();
        Assert.NotNull(page);

        return page;
    }
}
