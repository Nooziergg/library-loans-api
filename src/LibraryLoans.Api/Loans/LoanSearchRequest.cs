using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Loans;

namespace LibraryLoans.Api.Loans;

/// <summary>
/// Query-string filters for the loan register.
///
/// Every member is nullable because <c>[AsParameters]</c> treats a non-nullable one as a required
/// query parameter and ignores property initialisers: see the fuller note on
/// <c>BookSearchRequest</c>. Defaults are applied in <see cref="ToQuery"/>, where they are visible.
/// </summary>
public sealed record LoanSearchRequest
{
    public const int MaxPageSize = 100;

    private const int DefaultPageSize = 20;

    /// <summary>
    /// Highest page a caller may ask for.
    ///
    /// Bounded, and not merely for politeness: the offset is computed as
    /// <c>(Page - 1) * PageSize</c> in <c>int</c> arithmetic, which is unchecked. An unbounded page
    /// number multiplies past <c>int.MaxValue</c>, wraps negative, and PostgreSQL rejects the
    /// resulting negative OFFSET with an error nothing here translates: a 500 for a value the API
    /// itself declared valid. With this cap the largest product is ten million, and an absurd page
    /// gets the same 400 as every other malformed one.
    /// </summary>
    public const int MaxPage = 100_000;


    /// <summary>Restricts to one borrower's loans. An unknown id is an empty page, not a 404. This is a filter, not a subresource.</summary>
    public Guid? MemberId { get; init; }

    /// <summary>True for loans still out, false for those returned, absent for both.</summary>
    public bool? Active { get; init; }

    /// <summary>Still out and past its due date. Composes with the other filters.</summary>
    public bool? Overdue { get; init; }

    [AllowedValues(null, "loanedAt", "dueAt")]
    public string? SortBy { get; init; }

    public bool? Descending { get; init; }

    [Range(1, MaxPage)]
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
