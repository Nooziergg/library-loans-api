using System.Linq.Expressions;
using LibraryLoans.Application.Common;
using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Loans;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class LoanQueries(LibraryDbContext dbContext, TimeProvider timeProvider) : ILoanQueries
{
    private static readonly Dictionary<string, Expression<Func<Loan, object>>> SortableFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["loanedAt"] = loan => loan.LoanedAt,
            ["dueAt"] = loan => loan.DueAt,
        };

    public Task<LoanResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        // Straight into the response type, so the SQL carries only the columns the response
        // contains and no entity is materialised or tracked.
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

    public async Task<PagedResponse<LoanResponse>> SearchAsync(
        LoanSearchQuery query,
        CancellationToken cancellationToken)
    {
        var loans = dbContext.Loans.AsNoTracking();

        if (query.MemberId is { } memberId)
        {
            loans = loans.Where(loan => loan.MemberId == memberId);
        }

        if (query.Active is { } active)
        {
            loans = active
                ? loans.Where(loan => loan.ReturnedAt == null)
                : loans.Where(loan => loan.ReturnedAt != null);
        }

        if (query.Overdue)
        {
            // The rule comes from the aggregate that owns it rather than being restated here. This
            // is the entire reason Loan.OverdueAt is an expression: written out in this Where
            // clause, "overdue" would exist in two places and in two languages.
            //
            // The clock is injected, so a test can ask what was overdue at a chosen instant without
            // waiting for one.
            loans = loans.Where(Loan.OverdueAt(timeProvider.GetUtcNow()));
        }

        var totalCount = await loans.CountAsync(cancellationToken);

        var items = await ApplySort(loans, query.SortBy, query.Descending)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(loan => new LoanResponse(
                loan.Id,
                loan.BookCopyId,
                loan.MemberId,
                loan.LoanedAt,
                loan.DueAt,
                loan.ReturnedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<LoanResponse>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Always ends on the primary key, so ties in the sort column cannot let a row appear on two
    /// pages or on none. See the equivalent note in <see cref="BookQueries"/>.
    /// </summary>
    private static IQueryable<Loan> ApplySort(IQueryable<Loan> loans, string? sortBy, bool descending)
    {
        if (string.IsNullOrWhiteSpace(sortBy) || !SortableFields.TryGetValue(sortBy, out var keySelector))
        {
            return descending
                ? loans.OrderByDescending(loan => loan.Id)
                : loans.OrderBy(loan => loan.Id);
        }

        return descending
            ? loans.OrderByDescending(keySelector).ThenBy(loan => loan.Id)
            : loans.OrderBy(keySelector).ThenBy(loan => loan.Id);
    }
}
