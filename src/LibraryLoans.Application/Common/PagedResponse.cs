namespace LibraryLoans.Application.Common;

/// <summary>
/// One page of results, plus what a client needs to ask for the next one.
///
/// Every collection endpoint returns this. There is no unpaged variant, and that is deliberate: an
/// endpoint that returns everything is fine until the day the table is large, and by then it is
/// load-bearing in someone's integration.
/// </summary>
/// <param name="TotalCount">
/// Costs a second query against the same filter. Worth it — without it a client cannot render a
/// page control or know whether to keep going — but worth knowing about, and it is the first thing
/// to drop if a table ever grows large enough for the count to hurt.
///
/// It is also not transactionally consistent with <paramref name="Items"/>: the two are separate
/// statements, so a concurrent insert can leave the count one ahead of what the page contains.
/// Not fixed, because a transaction on a read costs more than the inconsistency does.
/// </param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>
    /// Safe from division by zero by construction: page size is bound by a <c>[Range]</c> with a
    /// lower limit of 1 on the request DTO, so it cannot arrive as zero. Do not relax that
    /// attribute without revisiting this.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}
