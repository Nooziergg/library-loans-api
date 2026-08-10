using LibraryLoans.Domain.Members;

namespace LibraryLoans.Application.Members;

public interface IMemberRepository
{
    Task<bool> ExistsWithMembershipNumberAsync(MembershipNumber membershipNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a member for a rule to interrogate. This is a read — the borrow path asks whether the
    /// member may borrow and does not modify them — so the implementation does not track it.
    /// </summary>
    Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Member member);
}
