using LibraryLoans.Application.Abstractions;
using LibraryLoans.Infrastructure.Auditing;
using LibraryLoans.Infrastructure.Idempotency;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Loans;
using LibraryLoans.Application.Members;
using LibraryLoans.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryLoans.Infrastructure;

/// <summary>
/// Binds the Application layer's ports to their EF Core implementations. This is the only
/// place the API project touches Infrastructure.
///
/// <para><b>One port this does not supply:</b> <c>IAuditContext</c>. It answers who the caller is,
/// which only the composition root can know, so the host registers it — <c>HttpAuditContext</c> in
/// the Api. A host that forgets fails when the context is first constructed, which is during
/// startup migration, with a message naming the missing service.</para>
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

        // Scoped, because the actor and correlation id it reads are per-request. The interceptor is
        // resolved from the same scope as the context it is attached to, below.
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<LibraryDbContext>((serviceProvider, options) => options
            // The audit trail, attached once, here. This is the whole registration: no handler
            // opts in, no entity carries an attribute, and a new aggregate is audited the day it
            // is mapped. See AuditSaveChangesInterceptor for why it writes inside the same
            // transaction as the change it describes.
            .AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>())
            .UseNpgsql(connectionString, npgsql =>
                // Transient faults — a database restart, a brief network partition — are
                // retried rather than surfaced. Two consequences worth knowing:
                //
                // 1. An execution strategy refuses user-initiated transactions unless they are
                //    wrapped in strategy.ExecuteAsync(...), because it cannot retry a block it
                //    does not control. Anything that opens an explicit transaction has to know
                //    this.
                // 2. A unique violation is NOT transient and is not retried, so this cannot
                //    turn a lost race into a duplicate insert.
                npgsql.EnableRetryOnFailure()));

        // Scoped, and the same instance for all three: the repository stages an entity on the
        // context and the unit of work saves that same context. Separate instances would mean
        // the save finds nothing to write.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Backs the Idempotency-Key header. Scoped like everything else here because it shares the
        // request's context — though every write it makes deliberately bypasses the change tracker;
        // see EfIdempotencyStore for why that is not an optimisation but a correctness requirement.
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();

        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IBookQueries, BookQueries>();
        services.AddScoped<IBookCopyRepository, BookCopyRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanQueries, LoanQueries>();
        services.AddScoped<IMemberQueries, MemberQueries>();
        services.AddScoped<IBookCopyQueries, BookCopyQueries>();

        return services;
    }
}
