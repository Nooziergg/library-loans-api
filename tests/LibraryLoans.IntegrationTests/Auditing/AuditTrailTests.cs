using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryLoans.IntegrationTests.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace LibraryLoans.IntegrationTests.Auditing;

/// <summary>
/// The audit trail, verified the way it will actually be used: a request goes in over HTTP, and the
/// question is what a person reading the table afterwards can find out.
///
/// <para>The rows are read with SQL rather than through the ORM that wrote them, deliberately and
/// for the same reason <c>ActiveLoanIndexTests</c> interrogates <c>pg_indexes</c> directly. Asking
/// EF Core to confirm what EF Core just did tests the two halves against each other; asking
/// PostgreSQL tests what is actually in the database. It also means these tests would survive
/// replacing the interceptor with a trigger, which is the point of testing an outcome rather than a
/// mechanism.</para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuditTrailTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private LibraryApiFactory _factory = null!;
    private HttpClient _client = null!;

    public AuditTrailTests(PostgresFixture postgres) => _postgres = postgres;

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

    private sealed record AuditRow(
        string EntityType,
        string EntityId,
        string Action,
        string Actor,
        string? CorrelationId,
        string? Changes);

    private static object AValidBook(string title = "The Hobbit") => new
    {
        isbn = "978-0-306-40615-7",
        title,
        author = "J. R. R. Tolkien",
        publishedYear = 1937,
    };

    private async Task<List<AuditRow>> ReadTrailAsync(string? entityType = null)
    {
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT entity_type, entity_id, action, actor, correlation_id, changes
            FROM audit_entries
            WHERE (@entityType IS NULL OR entity_type = @entityType)
            ORDER BY occurred_at, id
            """;
        // Typed explicitly: a bare null gives the server nothing to infer the parameter's type from,
        // and it answers with "could not determine data type" rather than with rows.
        command.Parameters.AddWithValue("entityType", NpgsqlDbType.Text, (object?)entityType ?? DBNull.Value);

        var rows = new List<AuditRow>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private static string IdFrom(HttpResponseMessage created) =>
        created.Headers.Location!.Segments[^1];

    [Fact]
    public async Task Records_who_created_what_and_under_which_request()
    {
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", "audit-create-1");

        var created = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var row = Assert.Single(await ReadTrailAsync("Book"));

        Assert.Equal("Created", row.Action);
        Assert.Equal(IdFrom(created), row.EntityId);

        // Honest rather than flattering. Nothing authenticates the caller, so the trail says so:
        // see IAuditContext.Actor.
        Assert.Equal("anonymous", row.Actor);

        // The join back to the logs: this is the same string the response header carried and every
        // log line for this request was written under.
        Assert.Equal("audit-create-1", row.CorrelationId);

        // Nothing, by the rule in AuditEntry.Changes: the created row is its own record.
        Assert.Null(row.Changes);
    }

    /// <summary>
    /// The old value is the thing the database cannot tell you, so it is the thing worth storing.
    /// </summary>
    [Fact]
    public async Task Records_the_before_and_after_values_of_an_update()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        var id = IdFrom(created);

        var updated = await _client.PutAsJsonAsync($"/api/v1/books/{id}", new
        {
            title = "There and Back Again",
            author = "J. R. R. Tolkien",
            publishedYear = 1937,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var rows = await ReadTrailAsync("Book");

        Assert.Equal(["Created", "Updated"], rows.Select(row => row.Action));

        using var changes = JsonDocument.Parse(rows[1].Changes!);
        var title = changes.RootElement.GetProperty("Title");

        Assert.Equal("The Hobbit", title.GetProperty("old").GetString());
        Assert.Equal("There and Back Again", title.GetProperty("new").GetString());

        // Only what changed. Author and PublishedYear were sent again with the same values, and a
        // trail that recorded them as changes would make every update look total.
        Assert.False(changes.RootElement.TryGetProperty("Author", out _));
        Assert.False(changes.RootElement.TryGetProperty("PublishedYear", out _));
    }

    /// <summary>
    /// The one case where the values are genuinely unrecoverable, so the one case that copies them
    /// in full. This also pins the value-converter handling: <c>Isbn</c> must appear as the string
    /// the column holds, not as the object the CLR holds.
    /// </summary>
    [Fact]
    public async Task Preserves_the_values_of_a_row_that_no_longer_exists()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        var id = IdFrom(created);

        var deleted = await _client.DeleteAsync($"/api/v1/books/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var row = (await ReadTrailAsync("Book")).Last();
        Assert.Equal("Deleted", row.Action);

        using var changes = JsonDocument.Parse(row.Changes!);

        Assert.Equal("The Hobbit", changes.RootElement.GetProperty("Title").GetString());
        Assert.Equal("9780306406157", changes.RootElement.GetProperty("Isbn").GetString());
    }

    /// <summary>
    /// **The reason the trail is written inside the unit of work rather than after it.**
    ///
    /// The second create loses to the unique index on ISBN and is reported as a 409. Its audit row
    /// was already staged in the change tracker when that happened, and it is rolled back with the
    /// insert it described, so the trail records the write that happened and not the one that was
    /// refused. An audit written after the commit, or to a separate store, would have recorded both
    /// and left the table claiming a book was created twice.
    /// </summary>
    [Fact]
    public async Task Writes_nothing_for_a_change_the_database_refused()
    {
        var first = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/v1/books", AValidBook());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var row = Assert.Single(await ReadTrailAsync("Book"));
        Assert.Equal(IdFrom(first), row.EntityId);
    }

    /// <summary>
    /// Reads cost the trail nothing, which is not a special case in the interceptor: every read path
    /// in this system is <c>AsNoTracking</c>, so nothing enters the change tracker to be described.
    /// </summary>
    [Fact]
    public async Task Writes_nothing_for_a_read()
    {
        await _client.GetAsync("/api/v1/books");
        await _client.GetAsync($"/api/v1/books/{Guid.CreateVersion7()}");
        await _client.GetAsync("/api/v1/loans?overdue=true");

        Assert.Empty(await ReadTrailAsync());
    }

    /// <summary>
    /// The claim that this is centralized rather than per-feature, stated as a test. Not one line of
    /// auditing code mentions <c>Member</c>; it is audited because it is an entity that was saved.
    /// </summary>
    [Fact]
    public async Task Audits_an_aggregate_no_auditing_code_mentions_by_name()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/members", new
        {
            membershipNumber = "M12345678",
            name = "Bilbo Baggins",
            email = "bilbo@example.com",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var row = Assert.Single(await ReadTrailAsync("Member"));

        Assert.Equal("Created", row.Action);
        Assert.Equal(IdFrom(created), row.EntityId);
    }
}
