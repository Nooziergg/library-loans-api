using LibraryLoans.Domain.Members;

namespace LibraryLoans.Application.Members;

/// <summary>Positional, for the reason given on <c>BookResponse</c>.</summary>
public sealed record MemberResponse(
    Guid Id,
    string MembershipNumber,
    string Name,
    string Email,
    string Status);

public static class MemberMappings
{
    public static MemberResponse ToResponse(this Member member) =>
        new(member.Id, member.MembershipNumber.Value, member.Name, member.Email, member.Status.ToString());
}

public sealed record RegisterMemberCommand(string? MembershipNumber, string? Name, string? Email);
