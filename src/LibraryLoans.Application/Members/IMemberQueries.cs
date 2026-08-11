using LibraryLoans.Application.Common;

namespace LibraryLoans.Application.Members;

/// <summary>
/// What a caller asked of the membership register.
/// </summary>
public sealed record MemberSearchQuery(string? Status, int Page, int PageSize);

/// <summary>
/// The read side for members — untracked, projected in SQL. Separate from
/// <see cref="IMemberRepository"/> for the reason given on <c>IBookQueries</c>: serving reads from
/// the repository would materialise the aggregate and its value objects only to map them in memory.
/// </summary>
public interface IMemberQueries
{
    Task<MemberResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResponse<MemberSummaryResponse>> SearchAsync(MemberSearchQuery query, CancellationToken cancellationToken);
}
