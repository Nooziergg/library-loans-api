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

    public void Add(Member member) => _added.Add(member);

    public void Seed(Member member) => _preexisting.Add(member);
}
