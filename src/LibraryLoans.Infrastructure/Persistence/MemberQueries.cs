using LibraryLoans.Application.Common;
using LibraryLoans.Application.Members;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Infrastructure.Persistence;

internal sealed class MemberQueries(LibraryDbContext dbContext) : IMemberQueries
{
    public Task<MemberResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Members
            .AsNoTracking()
            .Where(member => member.Id == id)
            .Select(member => new MemberResponse(
                member.Id,
                member.MembershipNumber.Value,
                member.Name,
                member.Email,
                member.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<MemberResponse>> SearchAsync(
        MemberSearchQuery query,
        CancellationToken cancellationToken)
    {
        var members = dbContext.Members.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // The status is stored as text, so this compares against the same strings the API
            // publishes and the response returns. Deliberately no index on this column: with two
            // distinct values PostgreSQL will scan regardless, and an index that is never chosen is
            // a maintenance cost pretending to be an optimisation.
            var status = query.Status;
            members = members.Where(member => member.Status.ToString() == status);
        }

        var totalCount = await members.CountAsync(cancellationToken);

        var items = await members
            .OrderBy(member => member.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(member => new MemberResponse(
                member.Id,
                member.MembershipNumber.Value,
                member.Name,
                member.Email,
                member.Status.ToString()))
            .ToListAsync(cancellationToken);

        return new PagedResponse<MemberResponse>(items, query.Page, query.PageSize, totalCount);
    }
}
