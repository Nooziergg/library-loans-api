using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using LibraryLoans.Infrastructure;

namespace LibraryLoans.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real application against the throwaway database.
///
/// Note what this does not do: it does not remove the registered <c>DbContext</c> and register
/// a different one. Overriding configuration and letting <c>AddInfrastructure</c> run exactly as
/// it does in production means the composition root itself is under test: retry policy,
/// connection handling, service lifetimes and all. Swapping the registration would produce a
/// suite that passes against wiring the deployed application never uses, which is the failure
/// mode where integration tests quietly stop testing anything.
/// </summary>
/// <param name="seed">
/// Whether to run the data seeder on startup. **Off by default, deliberately.**
///
/// Most test classes arrange exactly the rows they assert on, and seeding underneath them would
/// break them in a way that points at the wrong place: the seeded catalogue contains sixty titles
/// with their own ISBNs, barcodes and membership numbers, and a test posting its own fixture data
/// would collide on a unique index. The seeder keeps its natural keys clear of the ranges the tests
/// use (<c>9781...</c> ISBNs, <c>LIB-</c> barcodes, <c>M9...</c> members), but the cleanest guarantee
/// is that classes which do not need the seed never see it.
/// </param>
public sealed class LibraryApiFactory(string connectionString, bool seed = false)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            $"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}",
            connectionString);

        // The application migrates its own schema on startup, so the test database is built by
        // the same code path the container uses. A separate migration call in the fixture could
        // succeed while the startup path was broken.
        builder.UseSetting("MIGRATE_ON_STARTUP", "true");

        builder.UseSetting("SEED_ON_STARTUP", seed ? "true" : "false");
    }
}
