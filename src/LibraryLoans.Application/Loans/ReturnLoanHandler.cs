using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Loans;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Loans;

/// <summary>
/// Records a copy coming back.
///
/// Takes only the loan id. There is deliberately no member to check it against, with no
/// authentication there is no caller identity, so a <c>memberId</c> in the request would be an
/// unauthenticated claim and comparing it would enforce nothing while looking like it did. See
/// <c>docs/AUTHORIZATION.md</c>; a real library also accepts a returned book from whoever hands it
/// over.
/// </summary>
public sealed class ReturnLoanHandler(
    ILoanRepository loans,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ReturnLoanHandler> logger)
{
    public async Task<Result<LoanResponse>> HandleAsync(Guid loanId, CancellationToken cancellationToken)
    {
        // Tracked, unlike every other read in this codebase, because this one is about to mutate
        // what it loaded. The port name says so.
        var loan = await loans.FindForUpdateAsync(loanId, cancellationToken);
        if (loan is null)
        {
            return LoanErrors.NotFound(loanId);
        }

        var returned = loan.Return(timeProvider.GetUtcNow());
        if (!returned.IsSuccess)
        {
            return returned.Error;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        logger.LogInformation(
            "Returned loan {LoanId} on copy {BookCopyId}",
            loan.Id,
            loan.BookCopyId);

        return Result<LoanResponse>.Success(loan.ToResponse());
    }
}
