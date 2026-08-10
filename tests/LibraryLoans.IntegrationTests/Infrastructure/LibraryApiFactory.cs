using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using LibraryLoans.Infrastructure;

namespace LibraryLoans.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real application against the throwaway database.
///
/// Note what this does not do: it does not remove the registered <c>DbContext</c> and register
/// a different one. Overriding configuration and letting <c>AddInfrastructure</c> run exactly as
/// it does in production means the composition root itself is under test — retry policy,
/// connection handling, service lifetimes and all. Swapping the registration would produce a
/// suite that passes against wiring the deployed application never uses, which is the failure
/// mode where integration tests quietly stop testing anything.
/// </summary>
public sealed class LibraryApiFactory(string connectionString) : WebApplicationFactory<Program>
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
    }
}
