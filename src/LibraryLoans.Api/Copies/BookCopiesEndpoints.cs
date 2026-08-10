using System.ComponentModel.DataAnnotations;
using LibraryLoans.Api.Http;
using LibraryLoans.Application.Common;
using LibraryLoans.Application.Copies;
using LibraryLoans.Domain.Copies;
using Microsoft.AspNetCore.Http.HttpResults;

// The request property below is also called Barcode; inside that type the property wins.
using DomainBarcode = LibraryLoans.Domain.Copies.Barcode;

namespace LibraryLoans.Api.Copies;

public sealed record AddBookCopyRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(DomainBarcode.MaxLength)]
    public string? Barcode { get; init; }
}

/// <summary>Paging for the copies of a title. Nullable, per the note on <c>BookSearchRequest</c>.</summary>
public sealed record BookCopyListRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }
}

/// <summary>
/// Copies are addressed under the title they belong to — a copy has no meaning without one, and the
/// route says so.
/// </summary>
internal static class BookCopiesEndpoints
{
    public static RouteGroupBuilder MapBookCopies(this RouteGroupBuilder api)
    {
        var copies = api.MapGroup("/books/{bookId:guid}/copies").WithTags("Book copies");

        copies.MapGet("/", ListAsync)
            .AddEndpointFilter<ValidationFilter<BookCopyListRequest>>()
            .WithName("ListBookCopies")
            .WithSummary("Lists the physical copies of a title.");

        copies.MapPost("/", AddAsync)
            // .RequireAuthorization("RequireLibrarian")
            //   Adding stock is a staff operation. Not implemented — see docs/AUTHORIZATION.md.
            .AddEndpointFilter<ValidationFilter<AddBookCopyRequest>>()
            .WithName("AddBookCopy")
            .WithSummary("Adds a physical copy of a title.");

        return api;
    }

    private static async Task<Results<Ok<PagedResponse<BookCopyResponse>>, ProblemHttpResult>> ListAsync(
        Guid bookId,
        [AsParameters] BookCopyListRequest request,
        ListCopiesOfBookHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            bookId,
            request.Page ?? 1,
            request.PageSize ?? BookCopyListRequest.DefaultPageSize,
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Created<BookCopyResponse>, ProblemHttpResult>> AddAsync(
        Guid bookId,
        AddBookCopyRequest request,
        AddBookCopyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new AddBookCopyCommand(bookId, request.Barcode),
            cancellationToken);

        // As with members: 201 without a Location, because the read endpoint does not exist yet.
        return result.IsSuccess
            ? TypedResults.Created((string?)null, result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
