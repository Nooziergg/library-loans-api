using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Loans;

namespace LibraryLoans.Api.Loans;

/// <summary>
/// Query-string filters for the loan register.
///
/// Every member is nullable because <c>[AsParameters]</c> treats a non-nullable one as a required
/// query parameter and ignores property initialisers — see the fuller note on
/// <c>BookSearchRequest</c>. Defaults are applied in <see cref="ToQuery"/>, where they are visible.
/// </summary>
public sealed record LoanSearchRequest
{
    public const int MaxPageSize = 100;

    private const int DefaultPageSize = 20;

    /// <summary>Restricts to one borrower's loans. An unknown id is an empty page, not a 404 — this is a filter, not a subresource.</summary>
    public Guid? MemberId { get; init; }

    /// <summary>True for loans still out, false for those returned, absent for both.</summary>
    public bool? Active { get; init; }

    /// <summary>Still out and past its due date. Composes with the other filters.</summary>
    public bool? Overdue { get; init; }

    [AllowedValues(null, "loanedAt", "dueAt")]
    public string? SortBy { get; init; }

    public bool? Descending { get; init; }

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    public LoanSearchQuery ToQuery() => new(
        MemberId,
        Active,
        Overdue ?? false,
        SortBy,
        Descending ?? false,
        Page ?? 1,
        PageSize ?? DefaultPageSize);
}
