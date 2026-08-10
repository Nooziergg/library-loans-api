using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Copies;

/// <summary>
/// The barcode printed on a physical copy. Unique across every copy in the library.
///
/// Canonicalisation here is deliberately *less* aggressive than <see cref="Books.Isbn"/>'s, and the
/// asymmetry is a decision rather than an inconsistency:
///
/// <list type="bullet">
/// <item>Input is trimmed and upper-cased, so <c>abc-1</c> and <c>ABC-1</c> are the same barcode.
/// Without that, two rows could describe one physical object under a unique index claiming they
/// could not.</item>
/// <item>Separators are <b>not</b> stripped, so <c>ABC-1</c> and <c>ABC1</c> remain different. A
/// hyphen in an ISBN is a conventional grouping that carries no information, which is why
/// <c>Isbn</c> removes it. A barcode's characters *are* the printed label — collapsing them would
/// merge two labels a librarian can hold in their hand and tell apart.</item>
/// </list>
/// </summary>
public sealed record Barcode
{
    /// <summary>
    /// One constant serving as both the input bound and the column width, and here they genuinely
    /// must be the same number: a barcode has no fixed length, so nothing downstream pins the
    /// stored value to a narrower size the way <see cref="Members.MembershipNumber"/>'s format check
    /// does. If this exceeded the column, PostgreSQL would answer SQLSTATE 22001 — not a unique
    /// violation, so nothing translates it, and a public endpoint would return 500.
    /// </summary>
    public const int MaxLength = 32;

    private Barcode(string value) => Value = value;

    public string Value { get; }

    public static Result<Barcode> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return BookCopyErrors.BarcodeRequired();
        }

        // Bounded before any work proportional to the input, as in Isbn.Create.
        //
        // Checking before trimming is conservative rather than exact: trimming cannot lengthen a
        // string, so anything within the bound here is still within it afterwards, which is what
        // makes the pre-trim check safe as a cap. It does mean a value padded past the bound is
        // rejected even though it would have fitted once trimmed — accepted, because the bound
        // exists to stop an allocation being sized by a caller, and the alternative is allocating
        // first and checking second.
        if (input.Length > MaxLength)
        {
            return BookCopyErrors.BarcodeTooLong();
        }

        var candidate = input.Trim().ToUpperInvariant();

        if (candidate.Length == 0)
        {
            return BookCopyErrors.BarcodeRequired();
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return BookCopyErrors.BarcodeMalformed(input);
            }
        }

        return Result<Barcode>.Success(new Barcode(candidate));
    }

    /// <summary>The ORM materialization path, and nothing else.</summary>
    public static Barcode FromPersistedValue(string value) => new(value);

    public override string ToString() => Value;
}
