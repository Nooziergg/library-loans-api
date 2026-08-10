namespace LibraryLoans.Application.Books;

/// <summary>
/// What a caller asked of the catalogue, after the web layer has validated its shape.
///
/// The sort field arrives as a string, and the only reason that is safe is that the request DTO
/// constrains it to a published set with <c>[AllowedValues]</c> before it reaches here. A
/// caller-supplied sort expression flowing into a query builder is how ordering becomes an
/// injection surface; an allowlist is the only version of this that is safe by construction rather
/// than by care.
/// </summary>
public sealed record BookSearchQuery(
    string? Search,
    bool AvailableOnly,
    string? SortBy,
    bool Descending,
    int Page,
    int PageSize);
