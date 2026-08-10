using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LibraryLoans.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model without booting the API.
///
/// Migration scaffolding only needs to know which provider is in use — it never opens a
/// connection — so the string below is a placeholder and deliberately points at nothing real.
/// Without this, the tooling would start the web host, which would demand a configured
/// connection string and, in a container-less environment, fail for reasons that have nothing
/// to do with generating a migration.
///
/// It is used at design time only and has no effect at runtime, where options come from
/// <c>AddInfrastructure</c>.
/// </summary>
internal sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=library_design_time;Username=library;Password=design_time_only";

    public LibraryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql(DesignTimeConnectionString)
            .Options;

        return new LibraryDbContext(options);
    }
}
