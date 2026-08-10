namespace LibraryLoans.Application.Loans;

/// <summary>
/// The read side for loans — untracked, and projected to the response shape in SQL. Separate from
/// <see cref="ILoanRepository"/> for the reason given on <c>IBookQueries</c>: it makes those two
/// properties structural rather than something each author has to remember.
/// </summary>
public interface ILoanQueries
{
    Task<LoanResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
