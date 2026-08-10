using LibraryLoans.Api.Books;
using LibraryLoans.Api.Http;
using LibraryLoans.Application;
using LibraryLoans.Infrastructure;
using LibraryLoans.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// JSON lines on stdout, which is what a container platform collects. Scopes are enabled, so a
// correlation enricher can be added later without changing how anything is written.
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

app.Run();

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the real pipeline in
/// integration tests. Top-level statements otherwise generate an internal entry point.
/// </summary>
public partial class Program
{
}
