using System.ComponentModel.DataAnnotations;
using LibraryLoans.Api.Http;
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
    public static RouteGroupBuilder MapMembers(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/members").WithTags("Members");

        members.MapPost("/", RegisterAsync)
            // .RequireAuthorization("RequireLibrarian")
            //   Registering a borrower is a staff operation. Authorization is not implemented —
            //   see the seam in Program.cs and docs/AUTHORIZATION.md.
            .AddEndpointFilter<ValidationFilter<RegisterMemberRequest>>()
            .WithName("RegisterMember")
            .WithSummary("Registers a borrower.");

        return api;
    }

    private static async Task<Results<Created<MemberResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterMemberRequest request,
        RegisterMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        // 201 with the created resource and deliberately no Location header: there is no
        // GET /members/{id} yet, and RFC 9110 permits omitting Location — a header pointing at a
        // route that answers 404 would be worse than none at all.
        return result.IsSuccess
            ? TypedResults.Created((string?)null, result.Value)
            : DomainErrorToHttp.ToProblem(result.Error);
    }
}
