using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Loans;

namespace LibraryLoans.UnitTests.Fakes;

/// <summary>
/// A hand-written stand-in for the loan repository. The two query methods answer from whatever the
/// test arranged, so a handler that inverts a flag or asks about the wrong entity fails here rather
/// than in an integration test that happens to hit the right arrangement.
/// </summary>
internal sealed class InMemoryLoanRepository : ILoanRepository
{
    private readonly List<Loan> _preexisting = [];
    private readonly List<Loan> _added = [];

    /// <summary>Only what the code under test staged — see <c>InMemoryBookRepository.Added</c>.</summary>
    public IReadOnlyList<Loan> Added => _added;

    /// <summary>Records which copy the handler asked about, so a transposition is visible.</summary>
    public Guid? CopyAskedAbout { get; private set; }

    /// <summary>Records which member the handler counted loans for.</summary>
    public Guid? MemberAskedAbout { get; private set; }

    public Task<bool> HasActiveLoanForCopyAsync(Guid bookCopyId, CancellationToken cancellationToken)
    {
        CopyAskedAbout = bookCopyId;

        return Task.FromResult(_preexisting.Any(loan => loan.BookCopyId == bookCopyId && loan.IsActive));
    }

    public Task<int> CountActiveLoansForMemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        MemberAskedAbout = memberId;

        return Task.FromResult(_preexisting.Count(loan => loan.MemberId == memberId && loan.IsActive));
    }

    public Task<Loan?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_preexisting.Concat(_added).FirstOrDefault(loan => loan.Id == id));

    public void Add(Loan loan) => _added.Add(loan);

    public void Seed(Loan loan) => _preexisting.Add(loan);
}
