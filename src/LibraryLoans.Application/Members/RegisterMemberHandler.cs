using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Members;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Members;

public sealed class RegisterMemberHandler(
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    ILogger<RegisterMemberHandler> logger)
{
    public async Task<Result<MemberResponse>> HandleAsync(
        RegisterMemberCommand command,
        CancellationToken cancellationToken)
    {
        var membershipNumber = MembershipNumber.Create(command.MembershipNumber);
        if (!membershipNumber.IsSuccess)
        {
            return membershipNumber.Error;
        }

        // The cheap, clear rejection for the ordinary case. Not what makes the rule true — two
        // registrations can pass this microseconds apart, and the unique index decides. The unit of
        // work translates that violation into this same error.
        if (await members.ExistsWithMembershipNumberAsync(membershipNumber.Value, cancellationToken))
        {
            return MemberErrors.DuplicateMembershipNumber();
        }

        var member = Member.Register(membershipNumber.Value, command.Name, command.Email);
        if (!member.IsSuccess)
        {
            return member.Error;
        }

        members.Add(member.Value);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        // Identifier only. A member's name, email address and membership number are personal data;
        // structured logs turn every parameter into a queryable field that outlives the request and
        // travels wherever logs travel, so none of those appear here. The id is meaningless outside
        // the database and is what an investigation actually needs.
        logger.LogInformation("Registered member {MemberId}", member.Value.Id);

        return Result<MemberResponse>.Success(member.Value.ToResponse());
    }
}
