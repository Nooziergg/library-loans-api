using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Loans;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class LoanRepository(LibraryDbContext dbContext) : ILoanRepository
{
    public Task<bool> HasActiveLoanForCopyAsync(Guid bookCopyId, CancellationToken cancellationToken) =>
        dbContext.Loans
            .AsNoTracking()
            .AnyAsync(loan => loan.BookCopyId == bookCopyId && loan.ReturnedAt == null, cancellationToken);

    public Task<int> CountActiveLoansForMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        dbContext.Loans
            .AsNoTracking()
            .CountAsync(loan => loan.MemberId == memberId && loan.ReturnedAt == null, cancellationToken);

    /// <summary>
    /// The one deliberately tracked read in this codebase. Returning a loan is a write that starts
    /// with a read, so the change tracker has to be holding the entity when
    /// <c>SaveChangesAsync</c> runs — with <c>AsNoTracking()</c> here, <c>Loan.Return</c> would
    /// mutate an object nothing is watching and the endpoint would report success having written
    /// nothing at all.
    /// </summary>
    public Task<Loan?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Loans.FirstOrDefaultAsync(loan => loan.Id == id, cancellationToken);

    public void Add(Loan loan) => dbContext.Loans.Add(loan);
}
