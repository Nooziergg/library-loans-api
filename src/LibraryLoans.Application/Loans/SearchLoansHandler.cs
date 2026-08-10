using LibraryLoans.Application.Common;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.Application.Loans;

public sealed class SearchLoansHandler(ILoanQueries loans)
{
    public async Task<Result<PagedResponse<LoanResponse>>> HandleAsync(
        LoanSearchQuery query,
        CancellationToken cancellationToken) =>
        Result<PagedResponse<LoanResponse>>.Success(await loans.SearchAsync(query, cancellationToken));
}
