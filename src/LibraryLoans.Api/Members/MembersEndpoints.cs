using System.ComponentModel.DataAnnotations;
using LibraryLoans.Api.Http;
using LibraryLoans.Application.Common;
using LibraryLoans.Application.Members;
using LibraryLoans.Domain.Members;
using Microsoft.AspNetCore.Http.HttpResults;

// The request property below is also called MembershipNumber, and inside that type the property
// wins. The alias keeps the attribute pointing at the domain constant. Same pattern as
// CreateBookRequest's DomainIsbn.
using DomainMembershipNumber = LibraryLoans.Domain.Members.MembershipNumber;

namespace LibraryLoans.Api.Members;

/// <summary>
/// The wire shape for registering a borrower. Limits come from the domain's own constants, so the
/// 400 and the 422 can never disagree about where the boundary is.
/// </summary>
public sealed record RegisterMemberRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(DomainMembershipNumber.MaxInputLength)]
    public string? MembershipNumber { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(Member.NameMaxLength)]
    public string? Name { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(Member.EmailMaxLength)]
    public string? Email { get; init; }

    public RegisterMemberCommand ToCommand() => new(MembershipNumber, Name, Email);
}

internal static class MembersEndpoints
{
    private const string GetMemberByIdRouteName = "GetMemberById";

    public static RouteGroupBuilder MapMembers(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/members").WithTags("Members");

        members.MapGet("/", SearchAsync)
            .AddEndpointFilter<ValidationFilter<MemberSearchRequest>>()
            .WithName("SearchMembers")
            .WithSummary("Lists borrowers, optionally filtered by status.");

        members.MapGet("/{id:guid}", GetByIdAsync)
            .WithName(GetMemberByIdRouteName)
            .WithSummary("Fetches a single borrower.");

        members.MapPost("/{id:guid}/suspend", SuspendAsync)
            // .RequireAuthorization("RequireLibrarian")
            //   Suspending a borrower is a staff action. Not implemented — docs/AUTHORIZATION.md.
            .WithName("SuspendMember")
            .WithSummary("Suspends a borrower, blocking new loans.");

        members.MapPost("/", RegisterAsync)
            // .RequireAuthorization("RequireLibrarian")
            //   Registering a borrower is a staff operation. Authorization is not implemented —
            //   see the seam in Program.cs and docs/AUTHORIZATION.md.
            .AddEndpointFilter<ValidationFilter<RegisterMemberRequest>>()
            .WithName("RegisterMember")
            .WithSummary("Registers a borrower.");

        return api;
    }

    private static async Task<Results<Ok<PagedResponse<MemberSummaryResponse>>, ProblemHttpResult>> SearchAsync(
        [AsParameters] MemberSearchRequest request,
        SearchMembersHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Ok<MemberResponse>, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        GetMemberByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<Ok<MemberResponse>, ProblemHttpResult>> SuspendAsync(
        Guid id,
        SuspendMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }

    private static async Task<Results<CreatedAtRoute<MemberResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterMemberRequest request,
        RegisterMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess
            ? TypedResults.CreatedAtRoute(result.Value, GetMemberByIdRouteName, new { id = result.Value.Id })
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
