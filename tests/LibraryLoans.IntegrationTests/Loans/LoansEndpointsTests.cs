using System.Net;
using System.Net.Http.Json;
using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Members;
using LibraryLoans.Infrastructure.Persistence;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.IntegrationTests.Loans;

/// <summary>
/// The invariant this whole system is arranged around, exercised over real HTTP against a real
/// PostgreSQL: a copy cannot be on two active loans at once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoansEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;
    private int _uniqueSuffix;

    public LoansEndpointsTests(PostgresFixture postgres) => _postgres = postgres;

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

    // -- The two that matter most --------------------------------------------------------------

    /// <summary>
    /// The test that makes the word <i>partial</i> mean something.
    ///
    /// If the index on loans were a plain <c>UNIQUE (book_copy_id)</c> rather than
    /// <c>UNIQUE (book_copy_id) WHERE returned_at IS NULL</c>, every other test in this suite would
    /// still pass, including the concurrency one, while the real behaviour of the library became
    /// "a copy can be borrowed once in its lifetime and never again". This is the only test that
    /// fails when the filter is missing, which is why it was written before the migration was
    /// scaffolded.
    /// </summary>
    [Fact]
    public async Task Borrows_the_same_copy_again_after_it_has_been_returned()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();

        var first = await BorrowAsync(memberId, copyId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstLoan = await first.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(firstLoan);

        var returned = await _client.PostAsync($"/api/v1/loans/{firstLoan.Id}/return", null);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        var second = await BorrowAsync(memberId, copyId);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    /// <summary>
    /// Two requests race for one copy. Both can pass the handler's in-memory check microseconds
    /// apart; only the partial unique index can decide between them.
    ///
    /// The row-count assertion is the half that is easy to leave out and the half that matters: one
    /// 201 and one 409 would also be observed if both inserts had somehow landed and the second
    /// response were a coincidence.
    /// </summary>
    [Fact]
    public async Task Allows_exactly_one_of_two_simultaneous_borrows_of_one_copy()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();

        var responses = await Task.WhenAll(
            BorrowAsync(memberId, copyId),
            BorrowAsync(memberId, copyId));

        var statuses = responses.Select(response => response.StatusCode).ToArray();

        Assert.Single(statuses, HttpStatusCode.Created);
        Assert.Single(statuses, HttpStatusCode.Conflict);

        var loser = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var problem = await loser.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.copy.already_on_loan", problem?.Extensions["code"]?.ToString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var activeLoans = await dbContext.Loans
            .AsNoTracking()
            .CountAsync(loan => loan.BookCopyId == copyId && loan.ReturnedAt == null);

        Assert.Equal(1, activeLoans);
    }

    // -- The rest of the invariants, over HTTP ------------------------------------------------

    [Fact]
    public async Task Borrowing_a_copy_that_is_already_out_is_a_conflict()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();
        var otherMemberId = await RegisterMemberAsync();

        Assert.Equal(HttpStatusCode.Created, (await BorrowAsync(memberId, copyId)).StatusCode);

        var second = await BorrowAsync(otherMemberId, copyId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.copy.already_on_loan", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Returning_a_loan_twice_is_a_conflict()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();
        var borrowed = await BorrowAsync(memberId, copyId);
        var loan = await borrowed.Content.ReadFromJsonAsync<LoanResponse>();
        Assert.NotNull(loan);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/v1/loans/{loan.Id}/return", null)).StatusCode);

        var second = await _client.PostAsync($"/api/v1/loans/{loan.Id}/return", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.already_returned", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task A_suspended_member_cannot_borrow()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();
        await SuspendAsync(memberId);

        var response = await BorrowAsync(memberId, copyId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.member.suspended", problem?.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task A_member_cannot_hold_more_than_the_maximum_active_loans()
    {
        var bookId = await CreateBookAsync();
        var memberId = await RegisterMemberAsync();

        for (var held = 0; held < 5; held++)
        {
            var copyId = await AddCopyAsync(bookId);
            Assert.Equal(HttpStatusCode.Created, (await BorrowAsync(memberId, copyId)).StatusCode);
        }

        var oneTooMany = await BorrowAsync(memberId, await AddCopyAsync(bookId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, oneTooMany.StatusCode);
        var problem = await oneTooMany.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("loan.member.at_loan_limit", problem?.Extensions["code"]?.ToString());
    }

    /// <summary>The loan period is policy, and the response is where a client observes it.</summary>
    [Fact]
    public async Task Sets_the_due_date_two_weeks_after_the_loan_date()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();

        var response = await BorrowAsync(memberId, copyId);
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>();

        Assert.NotNull(loan);
        Assert.Equal(loan.LoanedAt.AddDays(14), loan.DueAt);
        Assert.Null(loan.ReturnedAt);
    }

    /// <summary>
    /// The 201's Location points at a route that serves the same loan.
    ///
    /// The timestamps are compared with a tolerance rather than for exact equality, and the reason is
    /// worth recording because it surprised this test into failing: the 201 body carries the instant
    /// still in memory, at .NET's 100-nanosecond tick resolution, while the GET reads it back from a
    /// PostgreSQL <c>timestamptz</c>, which stores microseconds. The two differ below the digits
    /// either one prints, so they render identically and compare unequal.
    ///
    /// That is a property of the storage, not a defect, and it is left in place rather than papered
    /// over by truncating in the domain or by re-reading after every write. A client that stores a
    /// creation response and later compares it field-by-field to a fetch will see this, which is a
    /// good reason for the comparison to be on the identifier.
    ///
    /// The tolerance is ten ticks, one microsecond, because that is exactly what the cause
    /// permits, not a round number chosen for comfort. Anything looser would also pass if a handler
    /// re-read the clock, a timezone conversion crept in, or a rounding behaviour changed, none of
    /// which this test means to allow.
    ///
    /// Note that <c>BooksEndpointsTests</c> asserts full record equality for the same property.
    /// That is not an inconsistency: <c>BookResponse</c> carries no timestamp, so nothing there is
    /// subject to storage precision.
    /// </summary>
    [Fact]
    public async Task Serves_a_loan_back_from_the_location_header()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();

        var created = await BorrowAsync(memberId, copyId);
        Assert.NotNull(created.Headers.Location);

        var fetched = await _client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var createdLoan = await created.Content.ReadFromJsonAsync<LoanResponse>();
        var fetchedLoan = await fetched.Content.ReadFromJsonAsync<LoanResponse>();

        Assert.NotNull(createdLoan);
        Assert.NotNull(fetchedLoan);
        Assert.Equal(createdLoan.Id, fetchedLoan.Id);
        Assert.Equal(createdLoan.BookCopyId, fetchedLoan.BookCopyId);
        Assert.Equal(createdLoan.MemberId, fetchedLoan.MemberId);
        Assert.Null(fetchedLoan.ReturnedAt);
        Assert.True((createdLoan.LoanedAt - fetchedLoan.LoanedAt).Duration() <= TimeSpan.FromTicks(10));
        Assert.True((createdLoan.DueAt - fetchedLoan.DueAt).Duration() <= TimeSpan.FromTicks(10));
    }

    /// <summary>
    /// A missing field is a malformed request and must be answered as one. Before the request's ids
    /// were made nullable this returned 404 describing a copy of all zeros, because
    /// <c>[Required]</c> on a non-nullable <c>Guid</c> sees <c>Guid.Empty</c> and passes: a missing
    /// field reported as a missing resource, and a different status class from the one a literal
    /// null in the same position produces.
    /// </summary>
    [Fact]
    public async Task Rejects_a_request_missing_required_fields_with_400()
    {
        var withNothing = await _client.PostAsJsonAsync("/api/v1/loans", new { });
        Assert.Equal(HttpStatusCode.BadRequest, withNothing.StatusCode);

        var (copyId, _) = await ArrangeCopyAndMemberAsync();
        var withoutMember = await _client.PostAsJsonAsync("/api/v1/loans", new { bookCopyId = copyId });
        Assert.Equal(HttpStatusCode.BadRequest, withoutMember.StatusCode);
    }

    [Fact]
    public async Task Reports_an_unknown_copy_and_an_unknown_member_as_not_found()
    {
        var (copyId, memberId) = await ArrangeCopyAndMemberAsync();

        var unknownCopy = await BorrowAsync(memberId, Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, unknownCopy.StatusCode);
        Assert.Equal(
            "book_copy.not_found",
            (await unknownCopy.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString());

        var unknownMember = await BorrowAsync(Guid.CreateVersion7(), copyId);
        Assert.Equal(HttpStatusCode.NotFound, unknownMember.StatusCode);
        Assert.Equal(
            "member.not_found",
            (await unknownMember.Content.ReadFromJsonAsync<ProblemDetails>())?.Extensions["code"]?.ToString());
    }

    // -- Arrangement --------------------------------------------------------------------------

    private async Task<(Guid CopyId, Guid MemberId)> ArrangeCopyAndMemberAsync()
    {
        var bookId = await CreateBookAsync();
        return (await AddCopyAsync(bookId), await RegisterMemberAsync());
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
        var body = await response.Content.ReadFromJsonAsync<CreatedResourceId>();
        return body!.Id;
    }

    private async Task<Guid> AddCopyAsync(Guid bookId)
    {
        var suffix = Interlocked.Increment(ref _uniqueSuffix);
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/books/{bookId}/copies",
            new { barcode = $"COPY-{suffix:D4}" });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreatedResourceId>();
        return body!.Id;
    }

    private async Task<Guid> RegisterMemberAsync()
    {
        var suffix = Interlocked.Increment(ref _uniqueSuffix);
        var response = await _client.PostAsJsonAsync("/api/v1/members", new
        {
            membershipNumber = $"M{suffix:D8}",
            name = "A Borrower",
            email = "borrower@example.test",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreatedResourceId>();
        return body!.Id;
    }

    private Task<HttpResponseMessage> BorrowAsync(Guid memberId, Guid bookCopyId) =>
        _client.PostAsJsonAsync("/api/v1/loans", new { memberId, bookCopyId });

    /// <summary>
    /// Suspension has no endpoint in this phase, so the aggregate is driven directly. That is the
    /// point of the method existing on <see cref="Member"/> before it has a caller: without it, the
    /// rule preventing a suspended member from borrowing could not be arranged, and a guard that
    /// cannot be arranged cannot be tested.
    /// </summary>
    private async Task SuspendAsync(Guid memberId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var member = await dbContext.Members.FirstAsync(candidate => candidate.Id == memberId);
        Assert.True(member.Suspend().IsSuccess);

        await dbContext.SaveChangesAsync();
    }

    private sealed record CreatedResourceId(Guid Id);
}
