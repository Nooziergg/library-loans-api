using LibraryLoans.Api.Http;
using LibraryLoans.Application.Books;
using LibraryLoans.Application.Common;
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

        // Authorization is not implemented: see the seam in Program.cs and the design in
        // docs/AUTHORIZATION.md. The policy each endpoint would carry is noted inline, because
        // the answer differs per endpoint and deciding it once per endpoint at the time the
        // endpoint is written is how it stays consistent.

        books.MapPost("/", CreateAsync)
            // .RequireAuthorization("RequireLibrarian")
            //   Changing the catalogue is a staff operation. A borrower has no reason to be able
            //   to add a title, and "nobody would try" is not an access control.
            .AddEndpointFilter<ValidationFilter<CreateBookRequest>>()
            .WithName("CreateBook")
            .WithSummary("Adds a title to the catalogue.");

        books.MapGet("/", SearchAsync)
            // Shape is validated by the filter before this runs: the sort field against a
            // published allowlist, the page bounds against a range. A sort name that is not on the
            // list is a malformed request, so it is a 400 rather than a domain refusal.
            .AddEndpointFilter<ValidationFilter<BookSearchRequest>>()
            .WithName("SearchBooks")
            .WithSummary("Searches the catalogue by title, author or ISBN.");

        books.MapGet("/{id:guid}", GetByIdAsync)
            // Reading the catalogue needs only an authenticated caller, which the group-level
            // default-deny policy already provides, so no explicit policy here. Worth stating
            // rather than leaving blank: an endpoint with no authorization line should be
            // recognisably a decision, not an omission.
            .WithName(GetBookByIdRouteName)
            .WithSummary("Fetches a single title.");

        books.MapPut("/{id:guid}", UpdateAsync)
            // .RequireAuthorization("RequireLibrarian")
            .AddEndpointFilter<ValidationFilter<UpdateBookRequest>>()
            .WithName("UpdateBook")
            .WithSummary("Corrects a title's details. The ISBN is not editable.");

        books.MapDelete("/{id:guid}", DeleteAsync)
            // .RequireAuthorization("RequireLibrarian")
            .WithName("DeleteBook")
            .WithSummary("Removes a title and its copies, if it has never been borrowed.");

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

    private static async Task<Results<Ok<PagedResponse<BookResponse>>, ProblemHttpResult>> SearchAsync(
        [AsParameters] BookSearchRequest request,
        SearchBooksHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
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

    private static async Task<Results<Ok<BookResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateBookRequest request,
        UpdateBookHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        DeleteBookHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        // 204 rather than 200 with a body: there is nothing left to describe.
        return result.IsSuccess
            ? TypedResults.NoContent()
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
