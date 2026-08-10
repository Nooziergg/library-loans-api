using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Loans;

namespace LibraryLoans.Application.Loans;

/// <summary>
/// Reads a single loan. Thin, and still present for the reason <c>GetBookByIdHandler</c> gives:
/// turning "no row" into a described failure belongs here, so the endpoint stays a pure adapter.
/// </summary>
public sealed class GetLoanByIdHandler(ILoanQueries loans)
{
    public async Task<Result<LoanResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var loan = await loans.GetByIdAsync(id, cancellationToken);

        return loan is null
            ? LoanErrors.NotFound(id)
            : Result<LoanResponse>.Success(loan);
    }
}
