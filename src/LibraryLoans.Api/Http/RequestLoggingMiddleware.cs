using System.Diagnostics;

namespace LibraryLoans.Api.Http;

/// <summary>
/// One structured line per request — method, path, status, elapsed — and a correlation identifier
/// pushed as a log scope so every line written while handling that request carries it.
///
/// Without this, a service whose brief asks for relevant logs answers a live request with silence:
/// the framework's own request logging sits under <c>Microsoft.AspNetCore</c>, which is set to
/// Warning so that its two-lines-per-request chatter does not drown the log. Suppressing that and
/// replacing it with nothing is the version of this decision that goes unnoticed until an incident.
///
/// The correlation identifier is what turns a pile of lines into a story. It is the only way to
/// answer "these two borrow requests arrived four milliseconds apart — were they the same caller?",
/// which is the question the accepted race on the five-loan limit eventually forces someone to ask.
/// </summary>
internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    internal const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>
    /// Bounds an attacker-controlled value before it reaches the log. The JSON formatter escapes
    /// what it writes, so a newline cannot forge a log entry — but an unbounded header would still
    /// be copied into every line of the request, and log volume is a budget like any other.
    /// </summary>
    internal const int MaxCorrelationIdLength = 64;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = ResolveCorrelationId(httpContext);

        // Assigned to TraceIdentifier, which is where AddProblemDetails reads the correlationId it
        // puts in every error body. That is the point of the whole mechanism: the string in the
        // response header, the string in the failure the caller is looking at, and the string on
        // every log line written while serving them are one string, so a support conversation
        // starts with a grep instead of a timestamp and a guess.
        httpContext.TraceIdentifier = correlationId;

        // Safe to set directly rather than through OnStarting: this middleware is the outermost in
        // the pipeline, so nothing has written to the response yet.
        httpContext.Response.Headers[CorrelationIdHeader] = correlationId;

        var startedAt = Stopwatch.GetTimestamp();

        // The message-format overload rather than a dictionary: it yields the same structured
        // CorrelationId field, and its ToString is the readable "CorrelationId:abc123" that the JSON
        // formatter writes alongside it. A dictionary scope renders its own type name there.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            try
            {
                await next(httpContext);
            }
            finally
            {
                // finally, not a plain sequence: a request that throws past the exception handler
                // is exactly the one worth having a line for.
                Log(httpContext, Stopwatch.GetElapsedTime(startedAt));
            }
        }
    }

    private void Log(HttpContext httpContext, TimeSpan elapsed)
    {
        var statusCode = httpContext.Response.StatusCode;

        // A 5xx is already logged once, with its stack, by GlobalExceptionHandler. Repeating it at
        // Error here would double every fault in any alert that counts error-level lines, so the
        // summary line sits one level below the detail line it accompanies.
        //
        // Health probes go to Debug. An orchestrator polls liveness every few seconds forever, and
        // a reviewer tailing the log while calling the API should see their own request, not a
        // probe scrolling it away.
        var level = statusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Warning
            : IsProbe(httpContext.Request.Path)
                ? LogLevel.Debug
                : LogLevel.Information;

        if (!logger.IsEnabled(level))
        {
            return;
        }

        // The query string is deliberately absent. It is the part of a URL that carries whatever a
        // caller chose to put there, it is unbounded, and on an API that grows it is the first
        // place something sensitive turns up in a log nobody meant to hold it. The correlation
        // identifier is how a line joins back to the request that produced it.
        logger.Log(
            level,
            "{RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds} ms",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            statusCode,
            Math.Round(elapsed.TotalMilliseconds, 1));
    }

    private static bool IsProbe(PathString path) => path.StartsWithSegments("/health");

    /// <summary>
    /// Honours an inbound identifier so a request that crosses a service boundary keeps one, and
    /// falls back to the identifier ASP.NET Core already assigns.
    /// </summary>
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var inbound = httpContext.Request.Headers[CorrelationIdHeader];

        // Exactly one value. A repeated header is ambiguous, and picking one of them is how a
        // caller gets to decide which of two identifiers the logs will believe.
        if (inbound.Count == 1 && IsWellFormed(inbound[0]))
        {
            return inbound[0]!;
        }

        // Otherwise the ambient trace id, which ASP.NET Core already stamps on every log scope.
        // Reusing it means this identifier agrees with the framework's own field instead of sitting
        // beside it as a second way to find the same request. The format check is not decoration:
        // an Activity in the legacy hierarchical format has an all-zero TraceId, and a correlation
        // identifier that is the same thirty-two zeroes for every request is worse than none.
        return Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
            ? activity.TraceId.ToHexString()
            : httpContext.TraceIdentifier;
    }

    private static bool IsWellFormed(string? value) =>
        value is { Length: > 0 and <= MaxCorrelationIdLength } &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
