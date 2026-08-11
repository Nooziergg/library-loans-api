using LibraryLoans.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.UnitTests.Http;

/// <summary>
/// What is left after the request line moved to the framework's <c>UseHttpLogging</c>: returning an
/// identifier to the caller, accepting one they chose, and refusing to repeat one that is not safe
/// to repeat.
///
/// <para>There are deliberately no tests here for the log line itself — no assertion that a status
/// or a duration is recorded. That is framework behaviour now, and a test asserting it would be
/// testing ASP.NET Core rather than this repository.</para>
/// </summary>
public sealed class CorrelationMiddlewareTests
{
    [Fact]
    public async Task Returns_an_identifier_to_the_caller()
    {
        var httpContext = ARequest("GET", "/api/v1/books");

        await Invoke(new CapturingLogger<CorrelationMiddleware>(), httpContext);

        var returned = httpContext.Response.Headers[CorrelationMiddleware.CorrelationIdHeader].ToString();

        Assert.False(string.IsNullOrWhiteSpace(returned));

        // Also the trace identifier, so the correlationId in a ProblemDetails body is this string.
        Assert.Equal(returned, httpContext.TraceIdentifier);
    }

    [Fact]
    public async Task Honours_an_identifier_the_caller_supplied()
    {
        var logger = new CapturingLogger<CorrelationMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[CorrelationMiddleware.CorrelationIdHeader] = "req-0198f3c1_42";

        await Invoke(logger, httpContext);

        Assert.Equal(
            "req-0198f3c1_42",
            httpContext.Response.Headers[CorrelationMiddleware.CorrelationIdHeader].ToString());
        Assert.Equal("req-0198f3c1_42", httpContext.TraceIdentifier);

        // Filed under it too, which is the point of accepting a caller's label at all: it is the
        // string they will quote, so it has to be the string the log can be searched by.
        Assert.Equal("req-0198f3c1_42", logger.Scope("CorrelationId"));
    }

    /// <summary>
    /// The scope is the one thing here that could be pure duplication, so it is asserted not to
    /// happen. With no identifier supplied, the value is the trace id — which the framework has
    /// already attached to every line of the request as <c>TraceId</c>. Repeating it under a second
    /// name would widen the log without adding anything to it.
    /// </summary>
    [Fact]
    public async Task Adds_no_scope_of_its_own_when_the_identifier_is_one_the_framework_already_logs()
    {
        var logger = new CapturingLogger<CorrelationMiddleware>();

        await Invoke(logger, ARequest("GET", "/api/v1/books"));

        Assert.Null(logger.Scope("CorrelationId"));

        // The caller is still given one — the header does not depend on the scope.
        Assert.NotEmpty(logger.HttpContextHeader);
    }

    /// <summary>
    /// The header is attacker-controlled. The JSON formatter escapes what it writes, so a newline
    /// cannot forge a log entry — but an unbounded or unconstrained value would still be the
    /// caller's text appearing in our records, and echoed back in our response.
    /// </summary>
    [Theory]
    // A forged line, if anything downstream ever wrote logs as plain text.
    [InlineData("abc\n{\"level\":\"Information\",\"message\":\"nothing to see\"}")]
    // Longer than the bound.
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    // Empty, and outside the accepted alphabet.
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    public async Task Refuses_an_identifier_that_is_not_well_formed(string supplied)
    {
        var logger = new CapturingLogger<CorrelationMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[CorrelationMiddleware.CorrelationIdHeader] = supplied;

        await Invoke(logger, httpContext);

        // Replaced, not rejected: the request still succeeded, and the caller still got an
        // identifier — just not the one they tried to write into our logs.
        Assert.NotEqual(
            supplied,
            httpContext.Response.Headers[CorrelationMiddleware.CorrelationIdHeader].ToString());
        Assert.Null(logger.Scope("CorrelationId"));
    }

    /// <summary>
    /// Two values for one header is ambiguous, and choosing one of them lets a caller decide which
    /// of two identifiers the logs will believe.
    /// </summary>
    [Fact]
    public async Task Refuses_a_repeated_identifier_header()
    {
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[CorrelationMiddleware.CorrelationIdHeader] = new[] { "first", "second" };

        await Invoke(new CapturingLogger<CorrelationMiddleware>(), httpContext);

        var used = httpContext.Response.Headers[CorrelationMiddleware.CorrelationIdHeader].ToString();

        Assert.NotEqual("first", used);
        Assert.NotEqual("second", used);
    }

    private static DefaultHttpContext ARequest(string method, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        return httpContext;
    }

    private static Task Invoke(CapturingLogger<CorrelationMiddleware> logger, HttpContext httpContext)
    {
        logger.HttpContext = httpContext;

        var middleware = new CorrelationMiddleware(_ => Task.CompletedTask, logger);

        return middleware.InvokeAsync(httpContext);
    }

    /// <summary>
    /// Records the scopes that were open, rather than the entries written — this middleware writes
    /// none of its own. Hand-written rather than mocked, for the same reason the rest of this repo
    /// hand-writes its mapping: it is a dozen lines and the assertions read against captured facts.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<object?> _scopesOpened = [];

        public HttpContext? HttpContext { get; set; }

        public string HttpContextHeader =>
            HttpContext?.Response.Headers[CorrelationMiddleware.CorrelationIdHeader].ToString() ?? string.Empty;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            _scopesOpened.Add(state);
            return NullScope.Instance;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        /// <summary>Every scope ever opened, so a scope that was never pushed reads as null.</summary>
        public string? Scope(string name) => _scopesOpened
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .Where(pair => pair.Key == name)
            .Select(pair => pair.Value?.ToString())
            .FirstOrDefault(value => value is not null);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
