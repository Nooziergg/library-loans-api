using LibraryLoans.Api.Books;
using LibraryLoans.Api.Http;
using LibraryLoans.Application;
using LibraryLoans.Infrastructure;
using LibraryLoans.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Structured logs from the first commit: JSON lines on stdout, which is what a container
// platform collects. Correlation scopes are layered on in P4 without changing this.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

// RFC 7807 for every failure, including ones nobody anticipated. The handler deliberately
// reveals nothing about the exception it caught — see GlobalExceptionHandler.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

// Injected everywhere a current time is needed, so "is this loan overdue" is a test with a
// fake clock rather than one that waits for the wall clock to cooperate. First-party since
// .NET 8; there is no reason for DateTime.UtcNow to appear anywhere in this codebase.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// First in the pipeline, so anything thrown downstream becomes a ProblemDetails rather than
// reaching the framework's developer error page.
app.UseExceptionHandler();

// Off unless explicitly enabled; compose turns it on. See DatabaseMigrator for why production
// would not do this.
if (app.Configuration.GetValue<bool>("MIGRATE_ON_STARTUP"))
{
    // The token is passed, not defaulted. A shutdown signal during startup migration should
    // stop the migration, and "accepts a CancellationToken, then every caller passes default"
    // is on this project's list of things not to repeat.
    await app.Services.ApplyMigrationsAsync(app.Lifetime.ApplicationStopping);
}

// Liveness deliberately touches no dependency. If the database is briefly unreachable the
// process is still alive and should not be killed and restarted by the orchestrator — that is
// what the readiness probe is for, added in P4.
app.MapHealthChecks("/health/live");

// One versioned group that every feature hangs off. Established now rather than retrofitted:
// in P4 the default-deny authorization policy attaches to this single group instead of to
// every endpoint individually.
var api = app.MapGroup("/api/v1");
api.MapBooks();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the real pipeline in
/// integration tests. Top-level statements otherwise generate an internal entry point.
/// </summary>
public partial class Program
{
}
