using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Members;

namespace LibraryLoans.Api.Members;

/// <summary>
/// Query-string filters for the membership register. Nullable throughout, for the reason given on
/// <c>BookSearchRequest</c>.
/// </summary>
public sealed record MemberSearchRequest
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


    /// <summary>
    /// An allowlist rather than free text, so an unknown status is a 400 that names the permitted
    /// values instead of an empty page a caller has to guess the meaning of.
    /// </summary>
    [AllowedValues(null, "Active", "Suspended")]
    public string? Status { get; init; }

    [Range(1, MaxPage)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    public MemberSearchQuery ToQuery() => new(Status, Page ?? 1, PageSize ?? DefaultPageSize);
}
