using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LibraryLoans.IntegrationTests.Infrastructure;

namespace LibraryLoans.IntegrationTests.Idempotency;

/// <summary>
/// The promise the <c>Idempotency-Key</c> header makes: one key means one execution, however many
/// times the client sends it.
///
/// <para>Every assertion here is about what a retrying client observes, because that is the entire
/// feature. The header names are written out as literals rather than referenced from the API
/// assembly. They are a wire contract that clients hard-code, so renaming the constant should break
/// these tests rather than quietly follow along.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IdempotencyTests : IAsyncLifetime
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotency-Replayed";

    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public IdempotencyTests(PostgresFixture postgres) => _postgres = postgres;

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

    private static object ABook(string title = "The Hobbit", string isbn = "978-0-306-40615-7") => new
    {
        isbn,
        title,
        author = "J. R. R. Tolkien",
        publishedYear = 1937,
    };

    private Task<HttpResponseMessage> PostBookAsync(string? idempotencyKey, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/books")
        {
            Content = JsonContent.Create(payload),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostRawAsync(string idempotencyKey, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/books")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);

        return _client.SendAsync(request);
    }

    private async Task<int> CountBooksAsync()
    {
        using var document = JsonDocument.Parse(
            await _client.GetStringAsync("/api/v1/books"));

        return document.RootElement.GetProperty("totalCount").GetInt32();
    }

    /// <summary>
    /// The case the feature exists for: the client never saw the first response and sent the request
    /// again. It must get the original answer, and there must not be a second book.
    /// </summary>
    [Fact]
    public async Task Replays_the_original_response_when_a_request_is_retried()
    {
        var first = await PostBookAsync("retry-once-1", ABook());
        var second = await PostBookAsync("retry-once-1", ABook());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        // Byte-identical, including the id, which is the part that matters. A client that retried
        // and received a *different* id would have created one book and be holding a reference to
        // another.
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());

        Assert.False(first.Headers.Contains(ReplayedHeader));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(ReplayedHeader)));

        // A 201 whose Location is missing tells the client something was created and refuses to say
        // where. The first version of this feature stored only the status and the body, and it
        // passed every assertion above, which is what a test written to the implementation rather
        // than to the promise looks like.
        Assert.NotNull(first.Headers.Location);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        Assert.Equal(1, await CountBooksAsync());
    }

    /// <summary>
    /// Without a key the API behaves exactly as it did before this middleware existed. The mechanism
    /// is opt-in, and this is the test that keeps it that way.
    /// </summary>
    [Fact]
    public async Task Leaves_a_request_without_a_key_alone()
    {
        var first = await PostBookAsync(idempotencyKey: null, ABook());
        var second = await PostBookAsync(idempotencyKey: null, ABook());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The unique index on ISBN, not the idempotency store. Worth pinning: the domain's own
        // uniqueness rules are the real guarantee, and this middleware does not stand in for them.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        Assert.Equal(1, await CountBooksAsync());
    }

    /// <summary>
    /// A key reused for a genuinely different request is a client bug. Replaying the first response
    /// would tell the caller their second, different request had succeeded, so it is refused
    /// instead, and nothing is created.
    /// </summary>
    [Fact]
    public async Task Refuses_a_key_that_was_already_used_for_a_different_request()
    {
        var first = await PostBookAsync("reused-key-1", ABook());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostBookAsync("reused-key-1", ABook("A Different Book", "978-0-14-017739-8"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(1, await CountBooksAsync());
    }

    /// <summary>
    /// A refusal the client can act on is deterministic, so it is stored and replayed like any other
    /// response. The alternative, releasing the key on a 4xx, would let a client retry its way
    /// past a validation error with the same key and get a different answer the second time.
    /// </summary>
    [Fact]
    public async Task Replays_a_refusal_as_faithfully_as_a_success()
    {
        var first = await PostBookAsync("bad-isbn-1", ABook(isbn: "9780306406158"));
        var second = await PostBookAsync("bad-isbn-1", ABook(isbn: "9780306406158"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(ReplayedHeader)));

        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A key is attacker-controlled text that becomes a primary key, so it is checked before it gets
    /// anywhere near the database rather than being left for the column width to reject.
    ///
    /// <para>The empty-string case is absent on purpose: <c>HttpClient</c> drops a header with an
    /// empty value before it reaches the wire, so a test for it would be testing the client. The
    /// server-side check covers it: <c>IsWellFormed</c> requires a length above zero.</para>
    /// </summary>
    /// <summary>
    /// A malformed body fails during model binding, which throws, so whether this is stored depends
    /// entirely on where the middleware sits relative to the exception handler. Registered inside it,
    /// the throw unwinds past the middleware, the buffer is empty, the key is released, and the 400
    /// the handler produces afterwards is never stored: the documented rule "a 4xx is stored and
    /// replayed" silently would not hold for the commonest 4xx of all. This test is what pins the
    /// ordering, because nothing else in the suite would notice it changing.
    /// </summary>
    [Fact]
    public async Task Replays_a_response_the_exception_handler_produced()
    {
        var first = await PostRawAsync("malformed-1", "{not json at all");
        var second = await PostRawAsync("malformed-1", "{not json at all");

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(ReplayedHeader)));

        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("semi;colon")]
    [InlineData("slash/es")]
    public async Task Rejects_a_malformed_key_with_400(string key)
    {
        var response = await PostBookAsync(key, ABook());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountBooksAsync());
    }

    /// <summary>
    /// The bound exists so that an unbounded header cannot be written into a bounded column, where it
    /// would arrive as a database error rather than as the client mistake it is.
    /// </summary>
    [Fact]
    public async Task Rejects_a_key_longer_than_the_column_that_stores_it()
    {
        var response = await PostBookAsync(new string('k', 129), ABook());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountBooksAsync());
    }

    /// <summary>
    /// **The race the primary key arbitrates.**
    ///
    /// Two copies of one request, in flight together: an impatient client, or a proxy that retried
    /// before the first attempt finished. Both try to claim the key; PostgreSQL lets one win. The
    /// loser is told the request is in progress rather than being allowed to run, and either way
    /// exactly one book exists at the end.
    ///
    /// The second response is deliberately not pinned to a single status: whether the loser sees 409
    /// (the winner is still working) or a replayed 201 (the winner finished first) is a matter of
    /// microseconds, and a test that demanded one of them would be a test that failed on a slow
    /// machine for no reason. What must hold in both worlds is asserted.
    /// </summary>
    [Fact]
    public async Task Executes_once_when_two_copies_of_a_request_arrive_together()
    {
        var responses = await Task.WhenAll(
            PostBookAsync("concurrent-1", ABook()),
            PostBookAsync("concurrent-1", ABook()));

        var statuses = responses.Select(response => (int)response.StatusCode).ToArray();

        Assert.Contains(StatusCodes.Status201Created, statuses);
        Assert.DoesNotContain(statuses, status => status >= StatusCodes.Status500InternalServerError);
        Assert.All(statuses, status => Assert.Contains(
            status,
            (int[])[StatusCodes.Status201Created, StatusCodes.Status409Conflict]));

        Assert.Equal(1, await CountBooksAsync());
    }

    private static class StatusCodes
    {
        public const int Status201Created = 201;
        public const int Status409Conflict = 409;
        public const int Status500InternalServerError = 500;
    }
}
