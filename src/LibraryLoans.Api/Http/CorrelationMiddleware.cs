using System.Diagnostics;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Gives the caller an identifier they can quote back, and files the request's log lines under it
/// when that identifier is one the caller chose.
///
/// <para><b>This class is deliberately small, because most of what a correlation middleware usually
/// does is already in the box.</b> ASP.NET Core creates an <see cref="Activity"/> per request and the
/// host adds its <c>TraceId</c>, <c>SpanId</c> and <c>ParentId</c> to every log scope; the hosting
/// layer adds <c>RequestId</c> and <c>RequestPath</c>; <c>AddProblemDetails</c> already writes a
/// <c>traceId</c> into every error body; and an inbound <c>traceparent</c> header is parsed and
/// adopted automatically, so an identifier already survives a service hop without anyone writing
/// code. An earlier version of this file re-implemented all four and pushed a scope that repeated,
/// under a different name, a value the framework had already put on the line.</para>
///
/// <para>Two things genuinely are missing, and they are what is left here:</para>
/// <list type="number">
/// <item>Nothing returns an identifier to the caller. A support conversation starts with the person
/// who saw the failure, and they can only quote what they were given — so it goes in a response
/// header, and into the RFC 7807 body via <c>TraceIdentifier</c>.</item>
/// <item><c>traceparent</c> is a 55-character machine format. A caller who wants their own readable
/// label on a request — a batch name, a ticket number — has nowhere to put it, so
/// <c>X-Correlation-Id</c> is accepted as an alias.</item>
/// </list>
///
/// <para>The scope is pushed <i>only</i> for a caller-supplied identifier. When nobody supplied one
/// the value here is the trace id, which the framework has already attached to every line of this
/// request, and adding it again under a second name would be duplication that makes the log wider
/// without making it say more.</para>
/// </summary>
internal sealed class CorrelationMiddleware(
    RequestDelegate next,
    ILogger<CorrelationMiddleware> logger)
{
    internal const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>
    /// Bounds an attacker-controlled value before it reaches the log. The JSON formatter escapes what
    /// it writes, so a newline cannot forge an entry — but an unbounded header would still be copied
    /// into every line of the request, and log volume is a budget like any other.
    /// </summary>
    internal const int MaxCorrelationIdLength = 64;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var supplied = SuppliedCorrelationId(httpContext);
        var correlationId = supplied ?? AmbientTraceId(httpContext);

        // Where AddProblemDetails reads the correlationId it puts in every error body, so the string
        // in the header and the string in the failure the caller is looking at are one string.
        httpContext.TraceIdentifier = correlationId;

        // Safe to set directly rather than through OnStarting: this middleware is outermost, so
        // nothing has written to the response yet.
        httpContext.Response.Headers[CorrelationIdHeader] = correlationId;

        if (supplied is null)
        {
            // Nothing to add: this identifier is the trace id, and it is already on every line.
            await next(httpContext);
            return;
        }

        // The message-format overload rather than a dictionary: it yields the same structured
        // CorrelationId field, and its ToString is the readable "CorrelationId:abc123" that the JSON
        // formatter writes alongside it. A dictionary scope renders its own type name there.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", supplied))
        {
            await next(httpContext);
        }
    }

    /// <summary>
    /// The caller's own identifier, if they sent one this service is willing to repeat.
    ///
    /// A value that fails the check is replaced rather than rejected: the header is a convenience,
    /// and failing the request over it would turn a convenience into a new way to fail.
    /// </summary>
    private static string? SuppliedCorrelationId(HttpContext httpContext)
    {
        var inbound = httpContext.Request.Headers[CorrelationIdHeader];

        // Exactly one value. A repeated header is ambiguous, and picking one of them lets a caller
        // decide which of two identifiers the logs will believe.
        return inbound.Count == 1 && IsWellFormed(inbound[0]) ? inbound[0] : null;
    }

    /// <summary>
    /// The identifier the framework already established — from an inbound <c>traceparent</c> if the
    /// caller sent one, otherwise freshly minted for this request.
    ///
    /// The format check is not decoration: an <see cref="Activity"/> in the legacy hierarchical
    /// format has an all-zero <c>TraceId</c>, and an identifier that is the same thirty-two zeroes
    /// for every request is worse than none.
    /// </summary>
    private static string AmbientTraceId(HttpContext httpContext) =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
            ? activity.TraceId.ToHexString()
            : httpContext.TraceIdentifier;

    private static bool IsWellFormed(string? value) =>
        value is { Length: > 0 and <= MaxCorrelationIdLength } &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
