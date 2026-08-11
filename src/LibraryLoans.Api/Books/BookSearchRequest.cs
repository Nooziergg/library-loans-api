using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Books;

namespace LibraryLoans.Api.Books;

/// <summary>
/// Query-string parameters for searching the catalogue, bound with <c>[AsParameters]</c> and
/// validated by the same filter every request DTO goes through.
///
/// Validating here rather than in the handler is what makes these 400s. A page number of zero or a
/// sort field nobody publishes is a malformed request, the caller has asked a question that does
/// not parse, as distinct from a well-formed request the domain refuses, which is a 422. That
/// split is the one the README describes, and applying it here keeps it true.
///
/// <para>
/// <b>Every member is nullable, and that is not a style choice.</b> <c>[AsParameters]</c> treats a
/// non-nullable member as a <i>required</i> query parameter and ignores property initialisers
/// entirely, so <c>public bool AvailableOnly { get; init; }</c> makes <c>?availableOnly=</c>
/// mandatory on every request: the endpoint throws before any of this validation runs. Nullable
/// members are optional; the defaults are applied in <see cref="ToQuery"/>, where they are visible.
/// </para>
/// </summary>
public sealed record BookSearchRequest
{
    /// <summary>Largest page a caller may ask for. The cap is the protection; the default below is only a convenience.</summary>
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
    /// Matched against title, author, or, if it parses as one, an ISBN in any spelling.
    ///
    /// Bounded, because the term reaches a LIKE pattern. Wildcards inside it are escaped before the
    /// query is built, so <c>%</c> and <c>_</c> match literally rather than acting as operators.
    /// </summary>
    [StringLength(100)]
    public string? Search { get; init; }

    /// <summary>Restricts results to titles with at least one copy not currently on loan.</summary>
    public bool? AvailableOnly { get; init; }

    /// <summary>
    /// An allowlist, not a hint. A caller-supplied sort expression reaching a query builder is how
    /// ordering becomes an injection surface; naming the permitted values in an attribute means an
    /// unknown one never reaches the application at all.
    ///
    /// <c>null</c> is listed deliberately: <c>AllowedValuesAttribute</c> compares against the set
    /// without special-casing null, so omitting it would reject every request that does not specify
    /// a sort, which is most of them.
    /// </summary>
    [AllowedValues(null, "title", "author", "publishedYear", "isbn")]
    public string? SortBy { get; init; }

    public bool? Descending { get; init; }

    [Range(1, MaxPage)]
    public int? Page { get; init; }

    /// <summary>
    /// Bounded rather than silently clamped. Serving 100 rows to a request for 10,000 would be a
    /// third behaviour for invalid input, alongside 400 and 422, with no stated rule covering it.
    /// A range says no in the same voice as every other malformed field, and the server still never
    /// returns more than <see cref="MaxPageSize"/> rows.
    /// </summary>
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; }

    public BookSearchQuery ToQuery() => new(
        Search,
        AvailableOnly ?? false,
        SortBy,
        Descending ?? false,
        Page ?? 1,
        PageSize ?? DefaultPageSize);
}
