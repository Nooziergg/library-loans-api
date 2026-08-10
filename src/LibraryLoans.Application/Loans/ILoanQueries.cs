using LibraryLoans.Application.Common;

namespace LibraryLoans.Application.Loans;

/// <summary>
/// What a caller asked of the loan register. Each filter is independent and they compose: a request
/// for one member's overdue loans is both applied.
/// </summary>
public sealed record LoanSearchQuery(
    Guid? MemberId,
    bool? Active,
    bool Overdue,
    string? SortBy,
    bool Descending,
    int Page,
    int PageSize);

/// <summary>
/// The read side for loans — untracked, and projected to the response shape in SQL. Separate from
/// <see cref="ILoanRepository"/> for the reason given on <c>IBookQueries</c>: it makes those two
/// properties structural rather than something each author has to remember.
/// </summary>
public interface ILoanQueries
{
    Task<LoanResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResponse<LoanResponse>> SearchAsync(LoanSearchQuery query, CancellationToken cancellationToken);
}
