using System.Net.Http.Json;
using System.Text.Json;
using LibraryLoans.IntegrationTests.Infrastructure;
using Npgsql;

namespace LibraryLoans.IntegrationTests.Architecture;

/// <summary>
/// One request modifies one aggregate.
///
/// <para>An aggregate is meant to be the transactional consistency boundary, but nothing about
/// having four aggregate types stops a handler from saving two of them together. The rule is
/// therefore invisible: it holds because every handler written so far happens to respect it, and it
/// would break silently the first time one did not. This is the same trick already played on the
/// dependency rule, which is a test rather than a diagram for exactly the same reason.</para>
///
/// <para>The check needs no new machinery because the audit trail already records it. Every write
/// is stamped with the entity type it touched and the correlation identifier of the request that
/// caused it, so "how many aggregate types did one request modify" is a <c>GROUP BY</c>.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AggregateTransactionTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public AggregateTransactionTests(PostgresFixture postgres) => _postgres = postgres;

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

    /// <summary>
    /// Deleting a title removes its copies in the same transaction. That is a genuine composition
    /// with no meaningful intermediate state, so it is allowed, and it is named here rather than
    /// tolerated silently. A second entry in this set should be argued for, not just added.
    /// </summary>
    private static readonly HashSet<string> DeclaredMultiAggregateRequests = ["delete-a-book"];

    [Fact]
    public async Task Each_request_modifies_one_aggregate_except_where_declared()
    {
        await ExerciseTheWritePathsAsync();

        var offenders = new List<string>();

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT correlation_id, string_agg(DISTINCT entity_type, ', ' ORDER BY entity_type)
            FROM audit_entries
            WHERE correlation_id IS NOT NULL
            GROUP BY correlation_id
            HAVING count(DISTINCT entity_type) > 1
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var request = reader.GetString(0);
            if (!DeclaredMultiAggregateRequests.Contains(request))
            {
                offenders.Add($"{request} modified {reader.GetString(1)}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every write endpoint, each under a correlation identifier naming it, so a failure says which
    /// request broke the rule rather than only that one did.
    /// </summary>
    private async Task ExerciseTheWritePathsAsync()
    {
        var bookId = await PostAsync("create-a-book", "/api/v1/books", new
        {
            isbn = "9780306406157",
            title = "The Hobbit",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        var copyId = await PostAsync("add-a-copy", $"/api/v1/books/{bookId}/copies", new
        {
            barcode = "COPY-0001",
        });

        var memberId = await PostAsync("register-a-member", "/api/v1/members", new
        {
            membershipNumber = "M00000001",
            name = "A Borrower",
            email = "borrower@example.test",
        });

        var loanId = await PostAsync("borrow-a-copy", "/api/v1/loans", new
        {
            memberId,
            bookCopyId = copyId,
        });

        await SendAsync("return-a-loan", HttpMethod.Post, $"/api/v1/loans/{loanId}/return", null);
        await SendAsync("suspend-a-member", HttpMethod.Post, $"/api/v1/members/{memberId}/suspend", null);
        await SendAsync("update-a-book", HttpMethod.Put, $"/api/v1/books/{bookId}", new
        {
            title = "The Hobbit, or There and Back Again",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });

        // The delete needs a title of its own. The one above now has lending history, and
        // DELETE /books/{id} correctly refuses to erase that, so deleting it would test the refusal
        // rather than the transaction. This one has a copy and no loans, which is still the
        // two-aggregate case the exception exists for.
        var disposableBookId = await PostAsync("create-another-book", "/api/v1/books", new
        {
            isbn = "9780451524935",
            title = "Nineteen Eighty-Four",
            author = "George Orwell",
            publishedYear = 1949,
        });

        await PostAsync("add-another-copy", $"/api/v1/books/{disposableBookId}/copies", new
        {
            barcode = "COPY-0002",
        });

        await SendAsync("delete-a-book", HttpMethod.Delete, $"/api/v1/books/{disposableBookId}", null);
    }

    /// <summary>
    /// The new resource's id. Read from the body rather than from <c>Location</c>, because not
    /// every creating endpoint here returns one, and this test is not the place to assert that.
    /// </summary>
    private async Task<string> PostAsync(string correlationId, string path, object payload)
    {
        var response = await SendAsync(correlationId, HttpMethod.Post, path, payload);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string correlationId,
        HttpMethod method,
        string path,
        object? payload)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Correlation-Id", correlationId);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return response;
    }
}
