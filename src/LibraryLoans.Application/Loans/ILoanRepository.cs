using LibraryLoans.Domain.Loans;

namespace LibraryLoans.Application.Loans;

public interface ILoanRepository
{
    /// <summary>
    /// True when the copy is currently out.
    ///
    /// Named for its polarity rather than for the question a caller might prefer to ask. An
    /// <c>IsCopyAvailableAsync</c> would read more naturally at one call site and invert the meaning
    /// of the flag passed into <c>Loan.Open</c>, and the type system cannot catch that mistake
    /// because both are <c>bool</c>. Naming the positive fact keeps the direction unambiguous
    /// everywhere it travels.
    /// </summary>
    Task<bool> HasActiveLoanForCopyAsync(Guid bookCopyId, CancellationToken cancellationToken);

    Task<int> CountActiveLoansForMemberAsync(Guid memberId, CancellationToken cancellationToken);

    /// <summary>True when any copy of the title is currently out. A temporary obstacle to deletion.</summary>
    Task<bool> HasActiveLoanForBookAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// True when any loan — returned or not — references a copy of the title. A permanent obstacle
    /// to deletion, and a wider question than the one above: the foreign key refuses the delete for
    /// a loan returned years ago just as firmly as for one outstanding today.
    /// </summary>
    Task<bool> HasAnyLoanForBookAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a loan in order to change it — the one read path in this codebase that is deliberately
    /// <b>tracked</b>. Every other read uses <c>AsNoTracking()</c>; returning a loan is a write that
    /// begins with a read, so the change tracker has to see it. The name says so, because an
    /// untracked entity here would mutate happily in memory and save nothing.
    /// </summary>
    Task<Loan?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    void Add(Loan loan);
}
