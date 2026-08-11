using LibraryLoans.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.UnitTests.Http;

public sealed class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task Writes_one_line_carrying_method_path_status_and_elapsed()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("POST", "/api/v1/loans");

        await Invoke(logger, httpContext, respondWith: StatusCodes.Status201Created);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("POST", entry.Field("RequestMethod"));
        Assert.Equal("/api/v1/loans", entry.Field("RequestPath"));
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<int>(entry.Field("StatusCode")));
        Assert.True(Assert.IsType<double>(entry.Field("ElapsedMilliseconds")) >= 0);
    }

    /// <summary>
    /// The query string is not logged, and that is a rule rather than an oversight. It is the part
    /// of a URL a caller fills in, and on an API that grows it is where something sensitive first
    /// turns up in a log nobody meant to hold it.
    /// </summary>
    [Fact]
    public async Task Does_not_log_the_query_string()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/members");
        httpContext.Request.QueryString = new QueryString("?search=someone%40example.com");

        await Invoke(logger, httpContext);

        Assert.DoesNotContain("example.com", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line that matters most is the one for a request that failed, so it is written from a
    /// finally block rather than after a successful await.
    /// </summary>
    [Fact]
    public async Task Still_writes_a_line_when_the_pipeline_throws()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        var middleware = new RequestLoggingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(httpContext));

        Assert.Single(logger.Entries);
    }

    /// <summary>
    /// A fault is logged once at Error, with its stack, by <see cref="GlobalExceptionHandler"/>.
    /// If the summary line were also Error, every alert counting error-level lines would see two.
    /// </summary>
    [Fact]
    public async Task Summarises_a_server_fault_below_the_level_that_reported_it()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();

        await Invoke(
            logger,
            ARequest("GET", "/api/v1/books"),
            respondWith: StatusCodes.Status500InternalServerError);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Entries).Level);
    }

    /// <summary>
    /// An orchestrator polls liveness forever. At Information it would scroll away the request a
    /// reviewer is actually tailing the log to see.
    /// </summary>
    [Fact]
    public async Task Keeps_health_probes_out_of_the_default_log()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware> { MinimumLevel = LogLevel.Information };

        await Invoke(logger, ARequest("GET", "/health/live"));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Assigns_a_correlation_identifier_and_returns_it_to_the_caller()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");

        await Invoke(logger, httpContext);

        var returned = httpContext.Response.Headers[RequestLoggingMiddleware.CorrelationIdHeader].ToString();
        Assert.False(string.IsNullOrWhiteSpace(returned));

        // The same value the caller can quote back is the one the log line is filed under.
        Assert.Equal(returned, Assert.Single(logger.Entries).Scope("CorrelationId"));
    }

    [Fact]
    public async Task Honours_an_identifier_the_caller_supplied()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[RequestLoggingMiddleware.CorrelationIdHeader] = "req-0198f3c1_42";

        await Invoke(logger, httpContext);

        Assert.Equal("req-0198f3c1_42", Assert.Single(logger.Entries).Scope("CorrelationId"));
        // Also adopted as the trace identifier, so the traceId in a ProblemDetails body matches.
        Assert.Equal("req-0198f3c1_42", httpContext.TraceIdentifier);
    }

    /// <summary>
    /// The header is attacker-controlled. The JSON formatter escapes what it writes, so a newline
    /// cannot forge a log entry — but an unbounded or unconstrained value would still be copied
    /// into every line of the request, and would be the caller's text appearing in our records.
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
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[RequestLoggingMiddleware.CorrelationIdHeader] = supplied;

        await Invoke(logger, httpContext);

        Assert.NotEqual(supplied, Assert.Single(logger.Entries).Scope("CorrelationId"));
    }

    /// <summary>
    /// Two values for one header is ambiguous, and choosing one of them lets a caller decide which
    /// of two identifiers the logs will believe.
    /// </summary>
    [Fact]
    public async Task Refuses_a_repeated_identifier_header()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var httpContext = ARequest("GET", "/api/v1/books");
        httpContext.Request.Headers[RequestLoggingMiddleware.CorrelationIdHeader] = new[] { "first", "second" };

        await Invoke(logger, httpContext);

        var used = Assert.Single(logger.Entries).Scope("CorrelationId");
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

    private static Task Invoke(
        CapturingLogger<RequestLoggingMiddleware> logger,
        HttpContext httpContext,
        int respondWith = StatusCodes.Status200OK)
    {
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = respondWith;
                return Task.CompletedTask;
            },
            logger);

        return middleware.InvokeAsync(httpContext);
    }

    /// <summary>
    /// Hand-written rather than mocked, for the same reason the rest of this repo hand-writes its
    /// mapping: it is a dozen lines, and it makes the assertions read against captured facts.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<object?> _activeScopes = [];

        public List<Entry> Entries { get; } = [];

        public LogLevel MinimumLevel { get; init; } = LogLevel.Trace;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            _activeScopes.Add(state);
            return new ScopeHandle(_activeScopes);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                [.. _activeScopes]));

        private sealed class ScopeHandle(List<object?> scopes) : IDisposable
        {
            public void Dispose() => scopes.RemoveAt(scopes.Count - 1);
        }
    }

    private sealed record Entry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        IReadOnlyList<object?> Scopes)
    {
        public object? Field(string name) =>
            State.SingleOrDefault(pair => pair.Key == name).Value;

        public string? Scope(string name) => Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .Where(pair => pair.Key == name)
            .Select(pair => pair.Value?.ToString())
            .FirstOrDefault(value => value is not null);
    }
}
