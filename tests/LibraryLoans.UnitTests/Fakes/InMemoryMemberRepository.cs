using LibraryLoans.Application.Members;
using LibraryLoans.Domain.Members;

namespace LibraryLoans.UnitTests.Fakes;

internal sealed class InMemoryMemberRepository : IMemberRepository
{
    private readonly List<Member> _preexisting = [];
    private readonly List<Member> _added = [];

    public IReadOnlyList<Member> Added => _added;

    public Task<bool> ExistsWithMembershipNumberAsync(
        MembershipNumber membershipNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added)
            .Any(member => member.MembershipNumber == membershipNumber));

    public Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).FirstOrDefault(member => member.Id == id));

    /// <summary>
    /// The same objects <see cref="GetByIdAsync"/> returns. A fake has no change tracker, so the
    /// tracked/untracked distinction the real repository makes has nothing to model here — what
    /// matters is that a handler mutating the result sees its change, which it does.
    /// </summary>
    public Task<Member?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        GetByIdAsync(id, cancellationToken);

    public void Add(Member member) => _added.Add(member);

    public void Seed(Member member) => _preexisting.Add(member);
}
