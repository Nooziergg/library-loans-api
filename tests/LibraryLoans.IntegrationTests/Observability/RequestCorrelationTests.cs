using System.Net;
using System.Net.Http.Json;
using LibraryLoans.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.IntegrationTests.Observability;

/// <summary>
/// The correlation identifier is only worth having if it survives the real pipeline, so this
/// asserts it over HTTP rather than against the middleware in isolation.
///
/// The failing case is the one that matters: an identifier returned on successful responses but
/// missing from errors is useless, because the request a caller wants to ask about is the one that
/// went wrong.
///
/// The header name is written out rather than referenced from the API assembly on purpose. It is a
/// wire contract — a caller quoting an identifier back to support has no access to our constants —
/// so renaming the constant should fail this test rather than silently follow it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RequestCorrelationTests(PostgresFixture postgres)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    [Theory]
    // A route that exists, one that reads from the database, and one that does not exist at all —
    // the last is answered by routing before any endpoint runs.
    [InlineData("/health/live")]
    [InlineData("/api/v1/books")]
    [InlineData("/no/such/route")]
    public async Task Every_response_carries_a_correlation_identifier(string path)
    {
        await using var factory = new LibraryApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdHeader));

        Assert.False(string.IsNullOrWhiteSpace(correlationId));
    }

    /// <summary>
    /// A request that crosses a service boundary keeps the identifier it arrived with, which is the
    /// whole point of accepting one rather than always minting a fresh id.
    /// </summary>
    [Fact]
    public async Task Returns_the_identifier_the_caller_supplied()
    {
        await using var factory = new LibraryApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdHeader, "upstream-42");

        var response = await client.GetAsync("/api/v1/books");

        Assert.Equal("upstream-42", Assert.Single(response.Headers.GetValues(CorrelationIdHeader)));
    }

    /// <summary>
    /// The loop that makes the identifier worth having. A caller looking at a failure can read one
    /// string out of the body they already have, and it is the same string every log line for that
    /// request was written under — so a support report resolves to a grep rather than to a timestamp
    /// and a guess. A header alone would not do: clients routinely surface an error body and almost
    /// never surface response headers.
    /// </summary>
    [Fact]
    public async Task Puts_the_same_identifier_in_a_failure_body_as_in_the_header()
    {
        await using var factory = new LibraryApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdHeader, "support-ticket-7");

        var response = await client.GetAsync($"/api/v1/books/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("support-ticket-7", problem?.Extensions["correlationId"]?.ToString());
        Assert.Equal("support-ticket-7", Assert.Single(response.Headers.GetValues(CorrelationIdHeader)));
    }

    /// <summary>
    /// An identifier that fails its own rules is replaced, not rejected. The header is a logging
    /// convenience, and refusing the request over it would turn that convenience into a new way to
    /// fail — while echoing it unchecked would let a caller write arbitrary text into our records.
    /// </summary>
    [Fact]
    public async Task Replaces_an_identifier_that_is_not_well_formed_without_failing_the_request()
    {
        await using var factory = new LibraryApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationIdHeader,
            new string('a', 128));

        var response = await client.GetAsync("/api/v1/books");

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain(
            "aaaa",
            Assert.Single(response.Headers.GetValues(CorrelationIdHeader)),
            StringComparison.Ordinal);
    }
}
