using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.Api.Http;

/// <summary>
/// Turns any unhandled exception into an RFC 7807 response that says nothing about the
/// exception.
///
/// The detail the client receives is fixed text. Not <c>ex.Message</c>, not the type name, and
/// never a stack trace: those routinely carry connection strings, file paths, SQL fragments and
/// internal host names, and an error path is the least-watched way for them to leave the
/// process. Everything worth knowing is logged server-side, where it belongs.
///
/// This also means the response shape is identical in every environment. A Development-only
/// exception page would make the container reviewers run behave differently from production,
/// and the difference would only show up on the day it matters.
/// </summary>
internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>
    /// Non-standard, widely understood, and used here only so that logs and metrics can tell an
    /// abandoned request apart from a served one.
    /// </summary>
    internal const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A client that goes away mid-request cancels the token threaded down to the database,
        // and the resulting OperationCanceledException lands here. It is not a fault. Nobody is
        // listening, there is no socket left to write a response to, and recording it as an
        // error means the service's error rate measures how often users close tabs and how
        // often mobile clients lose signal — which is what wakes someone up at three in the
        // morning for nothing.
        //
        // The RequestAborted conjunct is the part that has to be there. Treating every
        // OperationCanceledException as a disconnect would swallow a genuine internal timeout as
        // though the caller had left, and that mistake is both worse and much harder to notice.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request {RequestMethod} {RequestPath} was abandoned by the client",
                httpContext.Request.Method,
                httpContext.Request.Path);

            // 499 is nginx's invention rather than an IANA-registered code, and no client will
            // ever read it — the connection is gone. It is set anyway because things on this
            // side do read it: request logging and the built-in request-duration metric both
            // record Response.StatusCode, and leaving it at 200 would file every abandoned
            // request as a success. A borrowed status code in a log is a smaller lie than a
            // wrong one.
            httpContext.Response.StatusCode = ClientClosedRequest;

            // Handled, and deliberately writes no body: there is nobody to write it to.
            return true;
        }

        // A body the framework could not read at all — malformed JSON, a string where a number
        // belongs, no body where one is required, or a payload over the size limit. That is the
        // caller's mistake, and it already carries the right status; answering 500 would both
        // mislead the caller and file their typo as a server fault in the error rate.
        //
        // It also has to be handled here rather than by a filter, because the failure happens
        // during model binding — before any endpoint filter runs, so the validation filter that
        // produces the other 400s never sees these.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogInformation(
                "Unreadable request body on {RequestMethod} {RequestPath}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Response.StatusCode = badRequest.StatusCode;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = badRequest.StatusCode,
                    Title = "The request could not be read.",
                    // Deliberately fixed text. The underlying message carries byte offsets and JSON
                    // paths, which is detail about our parsing rather than about their mistake, and
                    // this handler's rule is that no exception text reaches a client.
                    Detail = "The request body could not be parsed. Check that it is valid JSON and "
                             + "that each field has the expected type.",
                },
            });
        }

        logger.LogError(
            exception,
            "Unhandled exception handling {RequestMethod} {RequestPath}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error.",
                Detail = "The request could not be completed. The failure has been logged.",
            },
        });
    }
}
