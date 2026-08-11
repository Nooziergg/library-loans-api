using Microsoft.AspNetCore.HttpLogging;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Keeps liveness probes out of the request log.
///
/// <para>An orchestrator polls <c>/health/live</c> every few seconds for the life of the deployment.
/// Logged, that is the overwhelming majority of the lines in the stream, and it scrolls away the
/// request a reviewer is tailing the log to see. It is also the least informative traffic there is:
/// the interesting case, a probe that fails, shows up as an unhealthy container rather than as a
/// line nobody was reading.</para>
///
/// <para><c>IHttpLoggingInterceptor</c> is the first-party seam for this, added in .NET 9. It is the
/// reason the request line itself needs no custom middleware: the framework's
/// <c>UseHttpLogging</c> produces the line, and per-request decisions about it live here.</para>
/// </summary>
internal sealed class HealthProbeLoggingInterceptor : IHttpLoggingInterceptor
{
    private const string HealthPrefix = "/health";

    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        if (logContext.HttpContext.Request.Path.StartsWithSegments(HealthPrefix))
        {
            // Disabling every field is what suppresses the entry: with nothing enabled there is
            // nothing to write. Done on the request side so the work of collecting fields is never
            // started, rather than collected and discarded on the way out.
            logContext.LoggingFields = HttpLoggingFields.None;
        }

        return default;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext) => default;
}
