using LibraryLoans.Application.Loans;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class LoanQueries(LibraryDbContext dbContext) : ILoanQueries
{
    public Task<LoanResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        // Straight into the response type, so the SQL carries only the columns the response
        // contains and no entity is materialised or tracked. This is the shape every read path in
        // the project follows.
        dbContext.Loans
            .AsNoTracking()
            .Where(loan => loan.Id == id)
            .Select(loan => new LoanResponse(
                loan.Id,
                loan.BookCopyId,
                loan.MemberId,
                loan.LoanedAt,
                loan.DueAt,
                loan.ReturnedAt))
            .FirstOrDefaultAsync(cancellationToken);
}
