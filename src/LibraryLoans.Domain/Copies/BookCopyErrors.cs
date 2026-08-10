using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Copies;

/// <summary>
/// Every failure relating to a physical copy, defined once. As with books and members, the
/// duplicate-barcode error has two detection sites — the pre-check and the unique index — and both
/// must produce this identical value.
/// </summary>
public static class BookCopyErrors
{
    private const int MaxEchoedInputLength = 20;

    public static DomainError BarcodeRequired() =>
        DomainError.Validation("book_copy.barcode.required", "A barcode is required.");

    public static DomainError BarcodeTooLong() =>
        DomainError.Validation(
            "book_copy.barcode.too_long",
            $"A barcode may be at most {Barcode.MaxLength} characters.");

    public static DomainError BarcodeMalformed(string input) =>
        DomainError.Validation(
            "book_copy.barcode.malformed",
            $"'{Echo(input)}' is not a barcode. Expected letters, digits and hyphens only.");

    public static DomainError DuplicateBarcode() =>
        DomainError.Conflict("book_copy.barcode.duplicate", "A copy with this barcode already exists.");

    public static DomainError NotFound(Guid id) =>
        DomainError.NotFound("book_copy.not_found", $"No copy exists with id {id}.");

    private static string Echo(string input) =>
        input.Length <= MaxEchoedInputLength
            ? input
            : string.Concat(input.AsSpan(0, MaxEchoedInputLength), "…");
}
