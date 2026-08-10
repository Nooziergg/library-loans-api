using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Books;
using LibraryLoans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.Infrastructure;

/// <summary>
/// Binds the Application layer's ports to their EF Core implementations. This is the only
/// place the API project touches Infrastructure.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail at startup with a sentence that says what to fix. Registering a context with
            // no connection string would defer the failure to the first query, where it
            // surfaces as an opaque provider error on an unrelated request.
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set " +
                $"ConnectionStrings__{ConnectionStringName} (compose.yaml sets it for the " +
                "containerised run).");
        }

        services.AddDbContext<LibraryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                // Transient faults — a database restart, a brief network partition — are
                // retried rather than surfaced. Two consequences worth knowing:
                //
                // 1. An execution strategy refuses user-initiated transactions unless they are
                //    wrapped in strategy.ExecuteAsync(...), because it cannot retry a block it
                //    does not control. The first explicit transaction here will be the borrow
                //    path in P2.
                // 2. A unique violation is NOT transient and is not retried, so this cannot
                //    turn a lost race into a duplicate insert.
                npgsql.EnableRetryOnFailure()));

        // Scoped, and the same instance for all three: the repository stages an entity on the
        // context and the unit of work saves that same context. Separate instances would mean
        // the save finds nothing to write.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IBookQueries, BookQueries>();

        return services;
    }
}
