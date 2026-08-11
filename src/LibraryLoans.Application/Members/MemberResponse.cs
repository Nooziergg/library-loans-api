using LibraryLoans.Domain.Members;

namespace LibraryLoans.Application.Members;

/// <summary>
/// A single member, in full. Positional, for the reason given on <c>BookResponse</c>.
///
/// <para>Returned only by the endpoints that name one member: the read by id, the registration that
/// echoes back what the caller just sent, and the suspension. See
/// <see cref="MemberSummaryResponse"/> for why the collection does not use this shape.</para>
/// </summary>
public sealed record MemberResponse(
    Guid Id,
    string MembershipNumber,
    string Name,
    string Email,
    string Status);

/// <summary>
/// A member as the register lists them: identifiers and status, no name, no email address.
///
/// <para><b>Why the list is a different shape from the read.</b> This system logs a member's id and
/// never their name or email, on the stated rule that personal data does not belong in a log field
/// that outlives the request. That rule was being enforced in the logging layer and broken one line
/// later by the response: <c>GET /api/v1/members</c> is anonymous, pages up to a hundred at a time,
/// and returned every borrower's name and email address to whoever asked. Guarding the log while
/// handing the same data out over the wire is misplaced rigour.</para>
///
/// <para>The distinction that matters is <i>enumeration</i>, not secrecy. Reading one member by id
/// is a targeted lookup that requires already knowing a version-7 GUID; walking the collection is
/// bulk extraction of the whole membership, and it is the second one that turns a missing
/// authorization layer into a data breach. So the collection stops being identifying and the
/// single read keeps its detail.</para>
///
/// <para>When authentication arrives this can be revisited — a librarian has every business reason
/// to see names in a list. The point is that the shape is a decision with a reason, not an
/// accident of reusing whichever DTO was already there.</para>
/// </summary>
public sealed record MemberSummaryResponse(
    Guid Id,
    string MembershipNumber,
    string Status);

public static class MemberMappings
{
    public static MemberResponse ToResponse(this Member member) =>
        new(member.Id, member.MembershipNumber.Value, member.Name, member.Email, member.Status.ToString());
}

public sealed record RegisterMemberCommand(string? MembershipNumber, string? Name, string? Email);
