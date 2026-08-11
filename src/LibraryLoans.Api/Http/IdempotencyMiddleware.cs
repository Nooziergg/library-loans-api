using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using LibraryLoans.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Makes a retried <c>POST</c> safe: the same <c>Idempotency-Key</c> on the same request returns the
/// original response instead of doing the work a second time.
///
/// <para><b>The problem it solves is not theoretical.</b> A client that sends <c>POST /loans</c> and
/// times out does not know whether the loan was created: the request may have succeeded and the
/// response been lost. Its options are to retry and risk a duplicate, or to not retry and risk
/// losing the operation. Every payments and banking API answers this the same way, and this is that
/// answer: the client picks a key, and the server promises that one key means one execution.</para>
///
/// <para><b>Why the other methods are absent.</b> <c>GET</c>, <c>PUT</c> and <c>DELETE</c> are
/// already idempotent by definition, repeating them lands on the same state, so a key would add a
/// table write for nothing. <c>POST</c> is the only method whose repetition means a second thing
/// happening, which is exactly why HTTP declines to make it safe and leaves it to the API.</para>
///
/// <para><b>Opt-in.</b> A request without the header behaves exactly as it did before this class
/// existed. That is the honest default for a header the caller has to generate: forcing a key would
/// break every existing client to protect the ones that never retry.</para>
///
/// <para><b>What this is not.</b> It is not a substitute for the domain's own uniqueness rules. A
/// duplicate borrow with <i>no</i> key is still refused by the partial unique index on active loans,
/// and that remains the real guarantee. This makes a well-behaved client's retry pleasant, while
/// the index is what makes the invariant true regardless of who calls it or how.</para>
/// </summary>
internal sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetailsService,
    ILogger<IdempotencyMiddleware> logger)
{
    internal const string HeaderName = "Idempotency-Key";

    /// <summary>
    /// Set on a replayed response so a caller can tell "your retry was already done" from "this ran
    /// now". Without it the two are indistinguishable, which makes the mechanism impossible to
    /// observe from the outside, including from a test.
    /// </summary>
    internal const string ReplayedHeaderName = "Idempotency-Replayed";

    /// <summary>Matches the column width, so a key that passes here cannot be rejected by storage.</summary>
    internal const int MaxKeyLength = 128;

    /// <summary>
    /// Responses larger than this are served but not stored, and the key is released so a retry
    /// re-executes. The alternative is an unbounded blob per key, and a table whose size is set by
    /// the largest response anyone ever retried. Every response this API produces is orders of
    /// magnitude below it.
    /// </summary>
    internal const int MaxStoredBodyBytes = 64 * 1024;

    public async Task InvokeAsync(HttpContext httpContext, IIdempotencyStore store)
    {
        if (!HttpMethods.IsPost(httpContext.Request.Method) ||
            !httpContext.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            await next(httpContext);
            return;
        }

        // Exactly one value, within bounds, from an alphabet that cannot surprise anything
        // downstream. A repeated header is ambiguous and letting the caller pick which of two keys
        // applies is not a decision to delegate.
        if (header.Count != 1 || !IsWellFormed(header[0]))
        {
            await WriteProblemAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Invalid idempotency key.",
                $"{HeaderName} must be a single value of 1 to {MaxKeyLength} characters, using "
                + "letters, digits, hyphens or underscores.");
            return;
        }

        var key = header[0]!;
        var cancellationToken = httpContext.RequestAborted;
        var fingerprint = await FingerprintAsync(httpContext.Request, cancellationToken);

        var reservation = await store.ReserveAsync(key, fingerprint, cancellationToken);

        switch (reservation.Outcome)
        {
            case IdempotencyOutcome.Completed when reservation.Response is { } stored:
                logger.LogInformation(
                    "Replaying stored response {StatusCode} for idempotency key {IdempotencyKey}",
                    stored.StatusCode,
                    key);

                await ReplayAsync(httpContext, stored, cancellationToken);
                return;

            case IdempotencyOutcome.InProgress:
                logger.LogInformation(
                    "Rejected a duplicate of an in-flight request for idempotency key {IdempotencyKey}",
                    key);

                await WriteProblemAsync(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "Request already in progress.",
                    "A request with this idempotency key is still being processed. Retry shortly to "
                    + "receive its response.");
                return;

            case IdempotencyOutcome.FingerprintMismatch:
                logger.LogWarning(
                    "Idempotency key {IdempotencyKey} was reused for a different request",
                    key);

                // 422 rather than 409, following draft-ietf-httpapi-idempotency-key-header: the
                // request is well formed and the conflict is not with the resource's state but with
                // the caller's own earlier use of the key. Answering 200 with the first response
                // would silently discard the second request, which is the outcome worth avoiding.
                await WriteProblemAsync(
                    httpContext,
                    StatusCodes.Status422UnprocessableEntity,
                    "Idempotency key reused.",
                    "This idempotency key was already used for a different request. Use a new key.");
                return;

            case IdempotencyOutcome.Reserved:
            default:
                await ExecuteAndRememberAsync(httpContext, store, key, cancellationToken);
                return;
        }
    }

    /// <summary>
    /// Runs the request with the response captured, then stores what the caller was told.
    /// </summary>
    private async Task ExecuteAndRememberAsync(
        HttpContext httpContext,
        IIdempotencyStore store,
        string key,
        CancellationToken cancellationToken)
    {
        var originalBodyFeature = httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>();

        using var buffer = new MemoryStream();

        // Swapping the feature rather than assigning Response.Body: minimal APIs write through
        // BodyWriter, and only replacing the feature redirects both that and Body to the same place.
        httpContext.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(buffer));

        try
        {
            await next(httpContext);
        }
        catch
        {
            // Only reached by something the exception handler itself could not deal with, since it
            // runs inside this middleware. The key must still not stay claimed, or a fault would
            // make every retry of this request answer "in progress" until the row expired.
            httpContext.Features.Set(originalBodyFeature);

            // The release gets its own guard, and this is not defensive habit. It is the difference
            // between reporting the fault that happened and reporting a different one. If the
            // endpoint threw because the database went away, this release throws too, and an
            // unguarded await here would replace the original exception with the second one. The
            // sharp case is a client disconnect: the OperationCanceledException would be swapped for
            // a database error, GlobalExceptionHandler would no longer recognise it as an abandoned
            // request, and an ordinary disconnect would be logged as a server fault and counted in
            // the error rate. That handler has twenty lines of comment explaining why that must not
            // happen; this is where it would have happened anyway.
            try
            {
                await store.ReleaseAsync(key, CancellationToken.None);
            }
            catch (Exception releaseFailure)
            {
                logger.LogWarning(
                    releaseFailure,
                    "Could not release idempotency key {IdempotencyKey} after a failed request. It "
                    + "stays claimed until it expires, so a retry will be told the request is still "
                    + "in progress",
                    key);
            }

            throw;
        }

        httpContext.Features.Set(originalBodyFeature);

        var body = buffer.ToArray();
        var statusCode = httpContext.Response.StatusCode;

        // A 5xx is not an answer, it is an absence of one, and storing it would convert a momentary
        // fault into a permanent one for the client best behaved enough to retry with the same key.
        // A 4xx is stored: a malformed request is malformed every time, and replaying that verdict
        // costs the database nothing.
        if (statusCode >= StatusCodes.Status500InternalServerError || body.Length > MaxStoredBodyBytes)
        {
            await store.ReleaseAsync(key, CancellationToken.None);
        }
        else
        {
            await store.CompleteAsync(
                key,
                new IdempotentResponse(
                    statusCode,
                    httpContext.Response.ContentType,
                    CaptureHeaders(httpContext.Response),
                    body),
                CancellationToken.None);
        }

        await originalBodyFeature.Stream.WriteAsync(body, cancellationToken);
    }

    /// <summary>
    /// The headers a replay has to reproduce for the response to still mean what it meant.
    ///
    /// <c>Location</c> is the one that matters here, every creating endpoint returns
    /// <c>CreatedAtRoute</c>, and the other two are included because they identify the
    /// representation rather than the exchange. Everything else is deliberately not replayed: the
    /// rest of a response describes <i>this</i> call, and reissuing a stale <c>Date</c> or somebody
    /// else's <c>Set-Cookie</c> would be a bug with a much longer tail than a missing header.
    /// </summary>
    private static readonly string[] ReplayableHeaders =
    [
        HeaderNames.Location,
        HeaderNames.ETag,
        HeaderNames.ContentLanguage,
    ];

    private static Dictionary<string, string> CaptureHeaders(HttpResponse response)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in ReplayableHeaders)
        {
            if (response.Headers.TryGetValue(name, out var value) && !StringValues.IsNullOrEmpty(value))
            {
                captured[name] = value.ToString();
            }
        }

        return captured;
    }

    private static async Task ReplayAsync(
        HttpContext httpContext,
        IdempotentResponse stored,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = stored.StatusCode;

        if (!string.IsNullOrEmpty(stored.ContentType))
        {
            httpContext.Response.ContentType = stored.ContentType;
        }

        // Before the replay marker, so a stored header can never overwrite it.
        foreach (var (name, value) in stored.Headers)
        {
            httpContext.Response.Headers[name] = value;
        }

        httpContext.Response.Headers[ReplayedHeaderName] = "true";
        httpContext.Response.ContentLength = stored.Body.Length;

        await httpContext.Response.Body.WriteAsync(stored.Body, cancellationToken);
    }

    /// <summary>
    /// Identifies the request this key was claimed for: method, path and body.
    ///
    /// <para>The body has to be read to do it, which means buffering it: a request body is a
    /// forward-only stream, and the endpoint downstream still needs to read the same bytes.
    /// <c>EnableBuffering</c> is what makes reading it twice legal.</para>
    ///
    /// <para>SHA-256 is not doing security work here; it is a collision-resistant identity for a byte
    /// sequence. What matters is that two different requests do not fingerprint the same, because
    /// that would replay one caller's response to another caller's request.</para>
    /// </summary>
    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        hash.AppendData(Encoding.UTF8.GetBytes(request.Method));
        hash.AppendData(Encoding.UTF8.GetBytes(request.Path.Value ?? string.Empty));

        var rented = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(rented, cancellationToken)) > 0)
            {
                hash.AppendData(rented.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        // Rewound, or the endpoint downstream reads an empty body and every request looks malformed.
        request.Body.Position = 0;

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool IsWellFormed(string? value) =>
        value is { Length: > 0 and <= MaxKeyLength } &&
        value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private async Task WriteProblemAsync(HttpContext httpContext, int statusCode, string title, string detail)
    {
        httpContext.Response.StatusCode = statusCode;

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
            },
        });
    }
}
