using LibraryLoans.Api.Books;
using LibraryLoans.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LibraryLoans.UnitTests.Http;

public sealed class ValidationFilterTests
{
    private static CreateBookRequest AValidRequest() => new()
    {
        Isbn = "9780306406157",
        Title = "The Hobbit",
        Author = "J. R. R. Tolkien",
        PublishedYear = 1937,
    };

    [Fact]
    public async Task Passes_a_valid_request_through_to_the_endpoint()
    {
        var reachedEndpoint = false;
        var filter = new ValidationFilter<CreateBookRequest>();

        var result = await filter.InvokeAsync(
            new TestFilterContext(AValidRequest()),
            _ =>
            {
                reachedEndpoint = true;
                return ValueTask.FromResult<object?>(TypedResults.Ok());
            });

        Assert.True(reachedEndpoint);
        Assert.IsType<Ok>(result);
    }

    [Fact]
    public async Task Rejects_a_request_that_violates_its_annotations_without_calling_the_endpoint()
    {
        var reachedEndpoint = false;
        var filter = new ValidationFilter<CreateBookRequest>();
        var request = AValidRequest() with { Title = null };

        var result = await filter.InvokeAsync(
            new TestFilterContext(request),
            _ =>
            {
                reachedEndpoint = true;
                return ValueTask.FromResult<object?>(TypedResults.Ok());
            });

        Assert.False(reachedEndpoint);
        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.True(problem.ProblemDetails.Errors.ContainsKey(nameof(CreateBookRequest.Title)));
    }

    /// <summary>
    /// The lower bound on the published year is expressed as a DataAnnotation, so it is caught
    /// here as a 400 rather than reaching the domain. The upper bound depends on the current
    /// time and so cannot be, which is why both checks exist.
    /// </summary>
    [Fact]
    public async Task Rejects_a_published_year_below_the_domain_floor()
    {
        var filter = new ValidationFilter<CreateBookRequest>();
        var request = AValidRequest() with { PublishedYear = 1200 };

        var result = await filter.InvokeAsync(
            new TestFilterContext(request),
            _ => ValueTask.FromResult<object?>(TypedResults.Ok()));

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.True(problem.ProblemDetails.Errors.ContainsKey(nameof(CreateBookRequest.PublishedYear)));
    }

    [Fact]
    public async Task Reports_a_missing_body_as_a_client_error()
    {
        var reachedEndpoint = false;
        var filter = new ValidationFilter<CreateBookRequest>();

        var result = await filter.InvokeAsync(
            // Wrapped explicitly: a bare null would bind as the params array itself rather than
            // as a single null argument, which is the opposite of what this test is about.
            new TestFilterContext([null]),
            _ =>
            {
                reachedEndpoint = true;
                return ValueTask.FromResult<object?>(TypedResults.Ok());
            });

        Assert.False(reachedEndpoint);
        Assert.IsType<ValidationProblem>(result);
    }

    /// <summary>
    /// The finding this test exists for. The obvious implementation looks up the argument and
    /// falls through to <c>next()</c> when it is not found — which means a filter attached to
    /// the wrong endpoint silently validates nothing and every request sails past. A validation
    /// control that disables itself quietly is worse than no control, because the tests still
    /// pass. It must fail loudly instead, on the first request.
    /// </summary>
    [Fact]
    public async Task Refuses_to_run_on_an_endpoint_it_cannot_validate()
    {
        var reachedEndpoint = false;
        var filter = new ValidationFilter<CreateBookRequest>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await filter.InvokeAsync(
                new TestFilterContext("an argument of an entirely different type"),
                _ =>
                {
                    reachedEndpoint = true;
                    return ValueTask.FromResult<object?>(TypedResults.Ok());
                }));

        Assert.False(reachedEndpoint);
    }

    private sealed class TestFilterContext(params object?[] arguments) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = new DefaultHttpContext();

        public override IList<object?> Arguments { get; } = arguments;

        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }
}
