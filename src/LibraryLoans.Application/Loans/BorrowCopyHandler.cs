using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Members;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Loans;

/// <summary>
/// Borrowing a copy: the use case the whole system is arranged around.
///
/// Four sequential reads happen before the insert: the copy, the member, the member's active-loan
/// count, and whether this copy is already out. That is named rather than hidden, because a reviewer
/// will count them. The alternative is one query returning all four facts, which would mean
/// <c>Loan.Open</c> taking loose scalars instead of the aggregates whose rules it applies: moving
/// the rules out of the objects that own them to save three round trips at a library's request rate.
/// At a different rate the trade would go the other way, and that is worth knowing rather than
/// discovering.
/// </summary>
public sealed class BorrowCopyHandler(
    IBookCopyRepository copies,
    IMemberRepository members,
    ILoanRepository loans,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<BorrowCopyHandler> logger)
{
    public async Task<Result<LoanResponse>> HandleAsync(
        BorrowCopyCommand command,
        CancellationToken cancellationToken)
    {
        var copy = await copies.GetByIdAsync(command.BookCopyId, cancellationToken);
        if (copy is null)
        {
            return BookCopyErrors.NotFound(command.BookCopyId);
        }

        var member = await members.GetByIdAsync(command.MemberId, cancellationToken);
        if (member is null)
        {
            return MemberErrors.NotFound(command.MemberId);
        }

        var activeLoanCount = await loans.CountActiveLoansForMemberAsync(member.Id, cancellationToken);
        var copyHasActiveLoan = await loans.HasActiveLoanForCopyAsync(copy.Id, cancellationToken);

        // Named arguments, because two of these are an int and a bool sitting next to each other and
        // the compiler would accept them transposed.
        var loan = Loan.Open(
            copy,
            member,
            memberActiveLoanCount: activeLoanCount,
            copyHasActiveLoan: copyHasActiveLoan,
            now: timeProvider.GetUtcNow());

        if (!loan.IsSuccess)
        {
            return loan.Error;
        }

        loans.Add(loan.Value);

        // Where the race is actually settled. If another request opened a loan on this copy between
        // the check above and this line, the partial unique index rejects the insert and the unit of
        // work hands back LoanErrors.CopyAlreadyOnLoan(): byte for byte the error the in-memory
        // guard would have produced. The caller cannot tell which layer noticed.
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        logger.LogInformation(
            "Opened loan {LoanId} on copy {BookCopyId} for member {MemberId}, due {DueAt}",
            loan.Value.Id,
            loan.Value.BookCopyId,
            loan.Value.MemberId,
            loan.Value.DueAt);

        return Result<LoanResponse>.Success(loan.Value.ToResponse());
    }
}
