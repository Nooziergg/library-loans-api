using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Members;

/// <summary>
/// A library membership number: the letter <c>M</c> followed by eight digits.
///
/// Like <see cref="Books.Isbn"/>, the constructor is private and the only public entry point
/// returns <see cref="Result{T}"/>, so an instance that exists is one that satisfies the format.
///
/// The number is supplied by the caller rather than generated here. A real library would allocate
/// it from a sequence, which removes the possibility of a collision entirely; taking it as input
/// keeps the registration endpoint honest about the duplicate-conflict path, which is the same
/// pre-check-plus-unique-index pattern the catalogue uses for ISBNs.
/// </summary>
public sealed record MembershipNumber
{
    /// <summary>
    /// One constant, serving both the bound on input and the width of the column.
    ///
    /// <see cref="Books.Isbn"/> needs two — one for input, one for storage — because
    /// canonicalisation there strips separators and shortens the string. Here canonicalisation only
    /// trims and uppercases, neither of which can lengthen the value, so input length and stored
    /// length are the same number and must not be allowed to drift apart. If they did, a value
    /// object accepting more characters than the column holds would produce PostgreSQL's SQLSTATE
    /// 22001 on write. That is not a unique violation, so nothing translates it, and a public
    /// endpoint would answer 500 for input the caller controls.
    /// </summary>
    public const int Length = 9;

    /// <summary>
    /// The bound on raw input, before trimming. Its only job is to stop an allocation being sized by
    /// a caller — it is not a business rule, which is why it is comfortably larger than
    /// <see cref="Length"/> rather than tuned to some guess about how much whitespace is reasonable.
    ///
    /// Safe to exceed the column width here, unlike the barcode case, because this format is fixed
    /// length: the parser below requires the trimmed value to be exactly <see cref="Length"/>
    /// characters, so what reaches the database is always exactly the column's width.
    /// </summary>
    public const int MaxInputLength = 32;

    private const char Prefix = 'M';

    private MembershipNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<MembershipNumber> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return MemberErrors.MembershipNumberRequired();
        }

        // Bounded before anything is allocated from the input's length, for the reason spelled out
        // in Isbn.Create: an allocation sized by a caller is a denial of service waiting to happen.
        //
        // The slack over Length is for surrounding whitespace, which Trim removes. It cannot let an
        // over-long value reach the database: the format check below requires the trimmed value to
        // be exactly Length characters, so what gets stored is always exactly the column's width.
        if (input.Length > MaxInputLength)
        {
            return MemberErrors.MembershipNumberMalformed(input);
        }

        var candidate = input.Trim().ToUpperInvariant();

        if (candidate.Length != Length || candidate[0] != Prefix)
        {
            return MemberErrors.MembershipNumberMalformed(input);
        }

        for (var position = 1; position < candidate.Length; position++)
        {
            if (!char.IsAsciiDigit(candidate[position]))
            {
                return MemberErrors.MembershipNumberMalformed(input);
            }
        }

        return Result<MembershipNumber>.Success(new MembershipNumber(candidate));
    }

    /// <summary>
    /// The ORM materialization path, and nothing else. Bypasses validation deliberately: see the
    /// equivalent note on <see cref="Books.Isbn.FromPersistedValue"/>.
    /// </summary>
    public static MembershipNumber FromPersistedValue(string value) => new(value);

    public override string ToString() => Value;
}
