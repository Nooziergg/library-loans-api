using LibraryLoans.Application.Members;
using LibraryLoans.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class MemberRepository(LibraryDbContext dbContext) : IMemberRepository
{
    public Task<bool> ExistsWithMembershipNumberAsync(
        MembershipNumber membershipNumber,
        CancellationToken cancellationToken) =>
        dbContext.Members
            .AsNoTracking()
            .AnyAsync(member => member.MembershipNumber == membershipNumber, cancellationToken);

    /// <summary>
    /// Untracked: the borrow path asks this member whether they may borrow and does not change
    /// them. The whole aggregate is loaded rather than projected because <c>Loan.Open</c>
    /// interrogates it — which is what lets the guard read as <c>if (!member.CanBorrow)</c> instead
    /// of as a comparison against a loose flag.
    /// </summary>
    public Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(member => member.Id == id, cancellationToken);

    /// <summary>Tracked, because the caller is about to change what it loaded.</summary>
    public Task<Member?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Members.FirstOrDefaultAsync(member => member.Id == id, cancellationToken);

    public void Add(Member member) => dbContext.Members.Add(member);
}
