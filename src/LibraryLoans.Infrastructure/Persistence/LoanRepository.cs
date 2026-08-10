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

    /// <summary>
    /// Whether any copy of a title is currently out. Joins through copies rather than denormalising
    /// a book id onto loans — a loan is against a copy, and adding the title to it would create a
    /// second place the relationship is recorded.
    /// </summary>
    public Task<bool> HasActiveLoanForBookAsync(Guid bookId, CancellationToken cancellationToken) =>
        dbContext.Loans
            .AsNoTracking()
            .AnyAsync(
                loan => loan.ReturnedAt == null &&
                        dbContext.BookCopies.Any(copy => copy.Id == loan.BookCopyId && copy.BookId == bookId),
                cancellationToken);

    /// <summary>Whether any loan at all, returned or not, references a copy of the title.</summary>
    public Task<bool> HasAnyLoanForBookAsync(Guid bookId, CancellationToken cancellationToken) =>
        dbContext.Loans
            .AsNoTracking()
            .AnyAsync(
                loan => dbContext.BookCopies.Any(copy => copy.Id == loan.BookCopyId && copy.BookId == bookId),
                cancellationToken);

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
