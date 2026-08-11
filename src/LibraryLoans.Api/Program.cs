using LibraryLoans.Api.Books;
using LibraryLoans.Api.Copies;
using LibraryLoans.Api.Http;
using LibraryLoans.Api.Loans;
using LibraryLoans.Api.Members;
using LibraryLoans.Application;
using LibraryLoans.Infrastructure;
using LibraryLoans.Infrastructure.Persistence;
using LibraryLoans.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// JSON lines on stdout, which is what a container platform collects. Scopes are enabled because
// RequestLoggingMiddleware carries the correlation identifier in one — without this the scope is
// opened, costs an allocation, and is written nowhere.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

// RFC 7807 for every failure, including ones nobody anticipated. The handler deliberately
// reveals nothing about the exception it caught — see GlobalExceptionHandler.
builder.Services.AddProblemDetails(options =>
    // The one piece of correlation a client can act on. A caller reporting a failure quotes this,
    // and it is the same string the response header carried and every log line for that request was
    // written under, so the report resolves to a grep. RequestLoggingMiddleware puts it on
    // TraceIdentifier; the built-in traceId stays as the trace's own id, because they answer
    // different questions and collapsing them would lose the one that spans services.
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

// Describes the API without a UI package. The endpoints carry WithName and WithSummary, and the
// TypedResults return types give the generator its status codes and schemas for free — which is
// a side benefit of returning Results<T, ...> rather than IResult.
builder.Services.AddOpenApi();

// Injected everywhere a current time is needed, so "is this loan overdue" is a test with a
// fake clock rather than one that waits for the wall clock to cooperate. First-party since
// .NET 8; there is no reason for DateTime.UtcNow to appear anywhere in this codebase.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  AUTHENTICATION AND AUTHORIZATION — NOT IMPLEMENTED. This is the seam, described.
//
//  Every endpoint in this service is currently anonymous. That is a deliberate scope decision,
//  not an oversight, and docs/AUTHORIZATION.md explains the reasoning and the intended design.
//  The seam is marked here because "where would auth go" is the question worth answering, and
//  because an omission that is documented at the point it would live reads differently from one
//  a reader has to discover.
//
//  What would be registered here:
//
//      builder.Services
//          .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//          .AddJwtBearer(options =>
//          {
//              // Entra ID / any OIDC provider. Authority + Audience only — no signing key
//              // lives in this service, because validation uses the provider's published
//              // JWKS. Nothing to leak, nothing to rotate here.
//              options.Authority = builder.Configuration["Auth:Authority"];
//              options.Audience  = builder.Configuration["Auth:Audience"];
//              options.TokenValidationParameters = new()
//              {
//                  ValidateIssuer = true, ValidateAudience = true,
//                  ValidateLifetime = true, ValidateIssuerSigningKey = true,
//              };
//          });
//
//      builder.Services.AddAuthorization(options =>
//      {
//          // DEFAULT-DENY. This single line is the most important one in the block: every
//          // endpoint requires an authenticated caller unless it explicitly opts out, so a
//          // forgotten attribute produces a 401 rather than a silent hole. The inverse —
//          // middleware that allows a request when no rule matched — is a fail-open design,
//          // and it fails quietly, which is the worst combination available.
//          options.FallbackPolicy = new AuthorizationPolicyBuilder()
//              .RequireAuthenticatedUser()
//              .Build();
//
//          // ROLE RULES: coarse-grained, who may perform a class of operation at all.
//          options.AddPolicy("RequireLibrarian", policy => policy.RequireRole("librarian"));
//          options.AddPolicy("RequireMember",    policy => policy.RequireRole("member"));
//      });
//
//  PERMISSION RULES go somewhere different, and the distinction matters. "Is this caller a
//  librarian" is a role check and belongs in a policy. "Is this caller allowed to act on THIS
//  member's loans" depends on the resource, cannot be answered from claims alone, and belongs
//  in the handler as a domain rule — a member may borrow and return only as themselves, while
//  a librarian may act for anyone. Pushing resource-scoped decisions into policies is how they
//  end up duplicated per endpoint and inconsistent.
//
//  Note what would NOT change: the Domain and Application layers. No aggregate learns what a
//  role is. That is the dependency rule paying off rather than being asserted.
// ─────────────────────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// First, and the order relative to the exception handler is the whole point rather than a
// preference. Outermost means the status code this line reports is the one the client actually
// received — including the 500 or the 400 that UseExceptionHandler substitutes on the way out.
// Registered the other way round, every failed request would be logged as whatever the status was
// before the handler ran, which is the one case where an accurate log matters most. It also puts
// the correlation scope around the exception handler, so the line recording a fault and the line
// summarising the request that caused it share an identifier.
app.UseMiddleware<RequestLoggingMiddleware>();

// Anything thrown downstream becomes a ProblemDetails rather than reaching the framework's
// developer error page.
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

// Fills an empty database so the API is worth exploring the moment it starts. Idempotent — it
// checks for existing data first — so restarting never duplicates anything.
//
// One assumption worth stating: this is safe because compose runs a single replica. Two instances
// booting together would both find an empty database and both seed, and one would lose on a unique
// index and crash-loop. A production deployment would seed as a separate job for the same reason it
// migrates as one.
if (app.Configuration.GetValue<bool>("SEED_ON_STARTUP"))
{
    try
    {
        await app.Services.SeedAsync(app.Lifetime.ApplicationStopping);
    }
    catch (Exception exception)
    {
        // Logged loudly and then tolerated, unlike a failed migration, which is fatal because the
        // API cannot serve a request against a schema that is not there. Sample data is a
        // convenience: the service works without it, so a failure here should cost a reviewer some
        // rows and an obvious error in the log — not a container that restarts forever and takes
        // every endpoint down with it.
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("LibraryLoans.Seeding")
            .LogError(exception, "Seeding failed. The API will start with whatever data is present");
    }
}

// app.UseAuthentication();
// app.UseAuthorization();
//   ^ would go here, before endpoint routing. Order is not stylistic: authentication must run
//     before authorization, and both before the endpoint executes.

// Liveness deliberately touches no dependency. If the database is briefly unreachable the
// process is still alive and should not be killed and restarted by the orchestrator — that is
// what a readiness probe is for, and the two must not be conflated.
//
// This is one of the two endpoints that would carry .AllowAnonymous() under the default-deny
// policy above — an orchestrator's probe has no token to present. The other is the readiness
// probe. Everything else would require a caller.
app.MapHealthChecks("/health/live");

// Served at /openapi/v1.json. Deliberately no Swagger UI: that would mean a third-party package
// for a browser convenience, and the document is what tooling actually consumes.
app.MapOpenApi();

// One versioned group that every feature hangs off. Established up front rather than
// retrofitted: a cross-cutting concern such as an authorization policy then attaches to this
// single group in one line, instead of to every endpoint individually where one omission is a
// hole nobody notices.
var api = app.MapGroup("/api/v1");

// api.RequireAuthorization();
//   ^ one line, attached to the group, would put every versioned endpoint behind authentication
//     at once. This is the whole reason the group exists up front rather than being retrofitted:
//     the alternative is an attribute per endpoint, where the fifteenth one added under deadline
//     pressure is the one nobody remembers.

api.MapBooks();
api.MapBookCopies();
api.MapMembers();
api.MapLoans();

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the real pipeline in
/// integration tests. Top-level statements otherwise generate an internal entry point.
/// </summary>
public partial class Program
{
}
