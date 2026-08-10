using LibraryLoans.Api.Http;
using LibraryLoans.Application.Books;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LibraryLoans.Api.Books;

/// <summary>
/// Book endpoints.
///
/// Each method is an adapter and nothing more: bind the request, call one handler, turn the
/// result into a status code. There is no branching on business conditions here, because the
/// moment an endpoint starts deciding what is allowed, that rule stops being testable without
/// a web server and starts being invisible to every other entry point into the system.
/// </summary>
internal static class BooksEndpoints
{
    /// <summary>
    /// Named so the 201 can point at it without anyone hand-assembling a URL. A literal
    /// "/api/v1/books/{id}" would silently go stale the day the prefix or route changes.
    /// </summary>
    private const string GetBookByIdRouteName = "GetBookById";

    public static RouteGroupBuilder MapBooks(this RouteGroupBuilder api)
    {
        var books = api.MapGroup("/books").WithTags("Books");

        books.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationFilter<CreateBookRequest>>()
            .WithName("CreateBook")
            .WithSummary("Adds a title to the catalogue.");

        books.MapGet("/{id:guid}", GetByIdAsync)
            .WithName(GetBookByIdRouteName)
            .WithSummary("Fetches a single title.");

        return api;
    }

    private static async Task<Results<CreatedAtRoute<BookResponse>, ProblemHttpResult>> CreateAsync(
        CreateBookRequest request,
        CreateBookHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(
                result.Value,
                GetBookByIdRouteName,
                new { id = result.Value.Id })
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Ok<BookResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        GetBookByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
