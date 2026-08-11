using LibraryLoans.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryLoans.UnitTests.Http;

public sealed class GlobalExceptionHandlerTests
{
    /// <summary>
    /// A client hanging up is ordinary traffic, not a fault. If this regresses, the symptom is
    /// not a broken response — it is an error rate that tracks user behaviour, and an on-call
    /// rotation that learns to ignore the alert.
    /// </summary>
    [Fact]
    public async Task Treats_a_client_disconnect_as_ordinary_traffic()
    {
        var problemDetails = new RecordingProblemDetailsService();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = new CancellationToken(canceled: true),
        };

        var handled = await CreateHandler(problemDetails).TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            CancellationToken.None);

        Assert.True(handled);
        // No body written: the connection is gone, and a 500 would be a fabricated fault.
        Assert.False(problemDetails.WasCalled);
        // Recorded as abandoned rather than as a success, so request logs and the duration
        // metric do not count it as served.
        Assert.Equal(GlobalExceptionHandler.ClientClosedRequest, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// The conjunct that keeps the branch above honest. An internal timeout also surfaces as an
    /// <see cref="OperationCanceledException"/>, and swallowing it would hide a real failure
    /// behind a story about the client leaving.
    /// </summary>
    [Fact]
    public async Task Still_reports_a_cancellation_the_client_did_not_cause()
    {
        var problemDetails = new RecordingProblemDetailsService();
        var httpContext = new DefaultHttpContext();

        var handled = await CreateHandler(problemDetails).TryHandleAsync(
            httpContext,
            new OperationCanceledException(),
            CancellationToken.None);

        Assert.True(handled);
        Assert.True(problemDetails.WasCalled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
    }

    /// <summary>
    /// A body the framework could not read is the caller's mistake, not a server fault.
    ///
    /// This has to be handled here rather than by the validation filter, because binding fails
    /// before any endpoint filter runs — so the path that produces every other 400 never sees these.
    /// Answering 500 would tell a caller their valid-looking typo broke the server, and would file
    /// it in the error rate as though it had.
    /// </summary>
    [Fact]
    public async Task Answers_an_unreadable_request_body_with_the_status_it_carries()
    {
        var problemDetails = new RecordingProblemDetailsService();
        var httpContext = new DefaultHttpContext();

        var handled = await CreateHandler(problemDetails).TryHandleAsync(
            httpContext,
            new BadHttpRequestException("Failed to read parameter from the request body as JSON.", 400),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, problemDetails.LastProblemDetails?.Status);
    }

    /// <summary>Uses the status the exception carries, so a payload over the size limit is a 413.</summary>
    [Fact]
    public async Task Preserves_a_non_400_status_from_the_binding_failure()
    {
        var problemDetails = new RecordingProblemDetailsService();
        var httpContext = new DefaultHttpContext();

        await CreateHandler(problemDetails).TryHandleAsync(
            httpContext,
            new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, httpContext.Response.StatusCode);
    }

    /// <summary>The parser's own wording never reaches the caller — byte offsets and JSON paths included.</summary>
    [Fact]
    public async Task Does_not_leak_the_parser_message_for_an_unreadable_body()
    {
        var problemDetails = new RecordingProblemDetailsService();
        const string parserDetail = "'\\' is an invalid start of a property name. BytePositionInLine: 1.";

        await CreateHandler(problemDetails).TryHandleAsync(
            new DefaultHttpContext(),
            new BadHttpRequestException(parserDetail, 400),
            CancellationToken.None);

        Assert.DoesNotContain(
            "BytePositionInLine",
            problemDetails.LastProblemDetails?.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answers_an_unexpected_fault_with_a_500_that_reveals_nothing()
    {
        var problemDetails = new RecordingProblemDetailsService();
        var httpContext = new DefaultHttpContext();
        var secret = "Login failed for user 'sa'; Password=hunter2";

        await CreateHandler(problemDetails).TryHandleAsync(
            httpContext,
            new InvalidOperationException(secret),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        var written = problemDetails.LastProblemDetails;
        Assert.NotNull(written);

        // Asserted by equality, not by substring. A "does not contain 'sa'" check against a
        // fixed title proves nothing and would start failing the day someone writes the word
        // "unexpected fault, see logs" — the point is that the response is a constant, so
        // nothing from the exception can reach it by any route.
        Assert.Equal("Unexpected error.", written.Title);
        Assert.Equal(
            "The request could not be completed. The failure has been logged.",
            written.Detail);
        Assert.DoesNotContain(secret, written.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    private static GlobalExceptionHandler CreateHandler(IProblemDetailsService problemDetailsService) =>
        new(NullLogger<GlobalExceptionHandler>.Instance, problemDetailsService);

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public bool WasCalled { get; private set; }

        public Microsoft.AspNetCore.Mvc.ProblemDetails? LastProblemDetails { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            WasCalled = true;
            LastProblemDetails = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            WasCalled = true;
            LastProblemDetails = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }
}
