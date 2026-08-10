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
    /// An allowlist rather than free text, so an unknown status is a 400 that names the permitted
    /// values instead of an empty page a caller has to guess the meaning of.
    /// </summary>
    [AllowedValues(null, "Active", "Suspended")]
    public string? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    public MemberSearchQuery ToQuery() => new(Status, Page ?? 1, PageSize ?? DefaultPageSize);
}
