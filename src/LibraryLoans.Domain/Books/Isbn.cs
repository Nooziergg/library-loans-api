using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Books;

/// <summary>
/// A structurally valid book identifier, stored in a single canonical form.
///
/// Two things this type guarantees, both of which matter more than they first appear:
///
/// 1. <b>An invalid instance cannot exist.</b> The constructor is private and the only public
///    entry point returns <see cref="Result{T}"/>, so "is this ISBN valid" is answered once,
///    at the boundary, rather than re-asked defensively at every layer.
///
/// 2. <b>One book has one representation.</b> <c>0-306-40615-2</c> and
///    <c>978-0-306-40615-7</c> are the same book: the second is the first, re-encoded. Both
///    pass their own checksum, so a type that merely validated would accept both and the
///    unique index on the catalogue would enforce nothing meaningful. ISBN-10 input is
///    therefore converted to its ISBN-13 equivalent on the way in, and only the 13-digit form
///    is ever stored or compared.
/// </summary>
public sealed record Isbn
{
    /// <summary>Canonical length. Every stored ISBN is a 13-digit form.</summary>
    public const int Length = 13;

    /// <summary>
    /// The longest input this type will look at. A fully hyphenated ISBN-13 is 17 characters,
    /// so 32 is generous; the number is not the point. The point is that a bound exists before
    /// anything is allocated from the input's length: see <see cref="Create"/>.
    /// </summary>
    public const int MaxInputLength = 32;

    private const int Isbn10Length = 10;
    private const string Isbn13Prefix = "978";

    private Isbn(string value) => Value = value;

    /// <summary>The canonical 13-digit form, digits only.</summary>
    public string Value { get; }

    /// <summary>
    /// Parses and canonicalizes user input. Accepts ISBN-10 or ISBN-13, with or without the
    /// conventional hyphens and spaces.
    /// </summary>
    public static Result<Isbn> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return BookErrors.IsbnRequired();
        }

        // This bound has to be here, before StripSeparators, because that method allocates a
        // stack buffer sized from this string, and the string arrives straight from an HTTP
        // request body, so its length is chosen by whoever is calling.
        //
        // Without the check, a large enough value exhausts the stack. StackOverflowException
        // cannot be caught: not by a try/catch, not by the global exception handler, not by
        // anything. The CLR terminates the process, and every other request in flight dies with
        // it. One unauthenticated request, repeatable.
        //
        // It reads like an input-validation rule and it is one. It is also the only thing
        // standing between a public endpoint and a denial of service, which is why it lives in
        // the domain rather than in a request DTO attribute. The attribute exists too, but a
        // second caller (a seeder, a message consumer, a test) would not go through it.
        if (input.Length > MaxInputLength)
        {
            return BookErrors.IsbnMalformed(input);
        }

        var digits = StripSeparators(input);

        return digits.Length switch
        {
            Isbn10Length => FromIsbn10(digits),
            Length => FromIsbn13(digits),
            _ => BookErrors.IsbnMalformed(input),
        };
    }

    /// <summary>
    /// Rehydrates a value that has already been validated and canonicalized: the ORM
    /// materialization path, and nothing else.
    ///
    /// It bypasses validation on purpose. The alternative, re-running <see cref="Create"/> on
    /// every row read, would spend the cost of parsing on data that was validated on write,
    /// and would surface storage corruption as an opaque failure deep inside a query. If the
    /// column ever holds something this type would reject, that is a database integrity
    /// problem to find and fix, not a case for the read path to paper over.
    /// </summary>
    public static Isbn FromPersistedValue(string value) => new(value);

    public override string ToString() => Value;

    private static string StripSeparators(string input)
    {
        Span<char> buffer = stackalloc char[input.Length];
        var length = 0;

        foreach (var character in input)
        {
            if (character is '-' or ' ')
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return new string(buffer[..length]);
    }

    /// <summary>
    /// ISBN-10 is valid when the digits weighted 10, 9, ... 1 sum to a multiple of 11. The
    /// final position may be 'X', meaning ten, which is the whole reason the checksum works
    /// modulo a prime.
    /// </summary>
    private static Result<Isbn> FromIsbn10(string digits)
    {
        var checksum = 0;

        for (var position = 0; position < Isbn10Length; position++)
        {
            var character = digits[position];
            int digit;

            if (char.IsAsciiDigit(character))
            {
                digit = character - '0';
            }
            else if (character is 'X' && position == Isbn10Length - 1)
            {
                digit = 10;
            }
            else
            {
                return BookErrors.IsbnMalformed(digits);
            }

            checksum += digit * (Isbn10Length - position);
        }

        return checksum % 11 == 0
            ? Result<Isbn>.Success(new Isbn(ToIsbn13(digits)))
            : BookErrors.IsbnChecksumFailed(digits);
    }

    /// <summary>
    /// ISBN-13 is valid when the digits weighted alternately 1 and 3 sum to a multiple of ten.
    /// </summary>
    private static Result<Isbn> FromIsbn13(string digits)
    {
        if (!digits.All(char.IsAsciiDigit))
        {
            return BookErrors.IsbnMalformed(digits);
        }

        return WeightedSum(digits) % 10 == 0
            ? Result<Isbn>.Success(new Isbn(digits))
            : BookErrors.IsbnChecksumFailed(digits);
    }

    /// <summary>
    /// Re-encodes a validated ISBN-10 as ISBN-13: drop its check digit, prefix the 978
    /// registration group, and compute the check digit the 13-digit scheme requires.
    /// </summary>
    private static string ToIsbn13(string isbn10)
    {
        var body = Isbn13Prefix + isbn10[..(Isbn10Length - 1)];
        var checkDigit = (10 - (WeightedSum(body) % 10)) % 10;

        return body + (char)('0' + checkDigit);
    }

    private static int WeightedSum(string digits)
    {
        var sum = 0;

        for (var position = 0; position < digits.Length; position++)
        {
            sum += (digits[position] - '0') * (position % 2 == 0 ? 1 : 3);
        }

        return sum;
    }
}
