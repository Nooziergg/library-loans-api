using System.Net;
using LibraryLoans.IntegrationTests.Infrastructure;

namespace LibraryLoans.IntegrationTests.Health;

/// <summary>
/// Boots the real application pipeline and probes it over HTTP. This proves the composition
/// root actually composes — every service registration resolves, and the app reaches the point
/// of serving traffic.
///
/// It runs against the same throwaway database as the rest of the suite even though liveness
/// touches no dependency. Booting with no connection string configured would let this test pass
/// for the wrong reason: the DbContext is resolved lazily, so a broken registration would stay
/// invisible right up until the first real request.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Liveness_probe_reports_healthy()
    {
        await using var factory = new LibraryApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
