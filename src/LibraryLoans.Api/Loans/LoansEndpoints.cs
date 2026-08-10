using System.ComponentModel.DataAnnotations;
using LibraryLoans.Api.Http;
using LibraryLoans.Application.Loans;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LibraryLoans.Api.Loans;

/// <summary>
/// Nullable on purpose. <c>[Required]</c> on a non-nullable <c>Guid</c> is a no-op: the validator
/// sees <c>Guid.Empty</c> rather than null and passes, so an omitted field would reach the handler
/// as all zeros and come back as <c>404 book_copy.not_found</c> — a missing field reported as a
/// missing resource, and a different status class from the one a null in the same position
/// produces. Making them nullable is what lets the attribute mean what it says.
/// </summary>
public sealed record BorrowCopyRequest
{
    [Required]
    public Guid? MemberId { get; init; }

    [Required]
    public Guid? BookCopyId { get; init; }

    // Safe to dereference: the validation filter runs before the endpoint and has already rejected
    // the request if either is absent.
    public BorrowCopyCommand ToCommand() => new(MemberId!.Value, BookCopyId!.Value);
}

internal static class LoansEndpoints
{
    private const string GetLoanByIdRouteName = "GetLoanById";

    public static RouteGroupBuilder MapLoans(this RouteGroupBuilder api)
    {
        var loans = api.MapGroup("/loans").WithTags("Loans");

        loans.MapPost("/", BorrowAsync)
            // .RequireAuthorization("RequireMember")
            //   Not implemented. Note the consequence while it is not: memberId arrives in the
            //   request body, so with no authenticated caller anyone can borrow as anyone. The
            //   intended rule — a member may act only where the token's subject matches the
            //   memberId, while a librarian may act for any member — is specified in
            //   docs/AUTHORIZATION.md. It is deliberately not faked here: an identity check
            //   against a value the caller supplies enforces nothing while appearing to.
            .AddEndpointFilter<ValidationFilter<BorrowCopyRequest>>()
            .WithName("BorrowCopy")
            .WithSummary("Borrows a copy for a member.");

        loans.MapPost("/{id:guid}/return", ReturnAsync)
            // .RequireAuthorization()
            //   Takes the loan id and nothing else — see docs/AUTHORIZATION.md. A real library
            //   accepts a returned book from whoever hands it over, and with no authenticated
            //   caller there is nothing to compare a memberId against anyway.
            .WithName("ReturnLoan")
            .WithSummary("Records a borrowed copy coming back.");

        loans.MapGet("/{id:guid}", GetByIdAsync)
            // Reading a loan needs only an authenticated caller, which the group-level default-deny
            // policy would supply. Stated so the absence of a line here reads as a decision.
            .WithName(GetLoanByIdRouteName)
            .WithSummary("Fetches a single loan.");

        return api;
    }

    private static async Task<Results<CreatedAtRoute<LoanResponse>, ProblemHttpResult>> BorrowAsync(
        BorrowCopyRequest request,
        BorrowCopyHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, GetLoanByIdRouteName, new { id = result.Value.Id })
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Ok<LoanResponse>, ProblemHttpResult>> ReturnAsync(
        Guid id,
        ReturnLoanHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Ok<LoanResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        GetLoanByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
