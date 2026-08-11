using LibraryLoans.Api.Books;
using LibraryLoans.Api.Copies;
using LibraryLoans.Api.Http;
using LibraryLoans.Api.Loans;
using LibraryLoans.Api.Members;
using LibraryLoans.Application;
using LibraryLoans.Application.Abstractions;
using LibraryLoans.Infrastructure;
using LibraryLoans.Infrastructure.Persistence;
using LibraryLoans.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.HttpLogging;

var builder = WebApplication.CreateBuilder(args);

// JSON lines on stdout, which is what a container platform collects. Scopes are enabled because
// they are where the per-request identifiers live — TraceId and RequestId from the framework,
// CorrelationId from CorrelationMiddleware when a caller supplied one. Without this they are
// collected and written nowhere.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
});

// One line per request — method, path, status, duration — from the framework rather than from a
// middleware written here. CombineLogs is what makes it one entry instead of the two ("request
// starting" / "request finished") that HttpLogging emits by default.
//
// The field list is the whole payload, and what is absent is a decision: RequestQuery is not
// enabled. A query string is the part of a URL a caller fills in, and on an API that grows it is
// where a search term or an email address first turns up in a log nobody meant to hold one. Headers
// and bodies are likewise off — this is a summary line, not a capture.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;

    options.CombineLogs = true;
});

// Per-request decisions about that line. Today: liveness probes are not logged.
builder.Services.AddHttpLoggingInterceptor<HealthProbeLoggingInterceptor>();

// RFC 7807 for every failure, including ones nobody anticipated. The handler deliberately
// reveals nothing about the exception it caught — see GlobalExceptionHandler.
builder.Services.AddProblemDetails(options =>
    // The one piece of correlation a client can act on. A caller reporting a failure quotes this,
    // and it is the same string the response header carried and every log line for that request was
    // written under, so the report resolves to a grep. CorrelationMiddleware puts it on
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

// Who is making a change, for the audit trail. Registered here rather than inside
// AddInfrastructure because only the host knows what a caller is: this implementation reads the
// ambient HTTP request, and reports "system" when there is none — the startup migration and the
// seeder both write through the same audited path.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditContext, HttpAuditContext>();

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

// Outermost, so the correlation scope encloses everything that logs — including the exception
// handler, which means the line recording a fault and the line summarising the request that caused
// it carry the same identifier.
app.UseMiddleware<CorrelationMiddleware>();

// Then the request line, and its position relative to the exception handler is the point rather
// than a preference. Outside it means the status this line reports is the one the client actually
// received, including a 500 or 400 that UseExceptionHandler substituted on the way out. The other
// way round, every failed request would be logged with whatever the status was before the handler
// ran — the one case where an accurate log matters most.
app.UseHttpLogging();

// Outside the exception handler, which is a correction worth recording rather than quietly
// swapping: this was registered inside it first, and the ordering was wrong in a way that a passing
// test suite did not show. Inside, an endpoint that threw would unwind *through* this middleware —
// the buffered response would be empty, the key released on the way past, and the 400 or 500 the
// handler produced afterwards would be written to the real stream and never stored. So the rule
// "a 4xx is stored and replayed" quietly did not hold for a malformed body, which throws during
// model binding. Outside, the handler runs first and writes its ProblemDetails into the buffer, so
// what is stored is what the client actually received, whatever produced it.
//
// Buffering is also what lets the handler set a status at all: nothing has reached the wire yet, so
// Response.HasStarted is still false when it runs.
//
// Being outside the endpoints, meanwhile, is what makes one registration cover every POST there
// will ever be, rather than a filter each endpoint has to remember to attach.
app.UseMiddleware<IdempotencyMiddleware>();

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
