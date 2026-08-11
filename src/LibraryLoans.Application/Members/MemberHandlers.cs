using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Common;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Members;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Members;

public sealed class GetMemberByIdHandler(IMemberQueries members)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await members.GetByIdAsync(id, cancellationToken);

        return member is null
            ? MemberErrors.NotFound(id)
            : Result<MemberResponse>.Success(member);
    }
}

public sealed class SearchMembersHandler(IMemberQueries members)
{
    public async Task<Result<PagedResponse<MemberSummaryResponse>>> HandleAsync(
        MemberSearchQuery query,
        CancellationToken cancellationToken) =>
        Result<PagedResponse<MemberSummaryResponse>>.Success(await members.SearchAsync(query, cancellationToken));
}

/// <summary>
/// Suspends a borrower, blocking new loans without recalling what they already hold.
///
/// A named transition rather than a general update to a status field, matching <c>Loan.Return</c>.
/// The distinction matters: a <c>PATCH</c> that sets a status would let a caller move a member to
/// any value the enum happens to have, whereas this endpoint can only do the one thing the domain
/// has a rule for.
/// </summary>
public sealed class SuspendMemberHandler(
    IMemberRepository members,
    IUnitOfWork unitOfWork,
    ILogger<SuspendMemberHandler> logger)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        // Tracked: this is a write that begins with a read, like returning a loan.
        var member = await members.FindForUpdateAsync(id, cancellationToken);
        if (member is null)
        {
            return MemberErrors.NotFound(id);
        }

        var suspended = member.Suspend();
        if (!suspended.IsSuccess)
        {
            return suspended.Error;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        // The identifier and nothing else: a member's name, email and membership number are
        // personal data, and a structured log field outlives the request.
        logger.LogInformation("Suspended member {MemberId}", member.Id);

        return Result<MemberResponse>.Success(member.ToResponse());
    }
}
