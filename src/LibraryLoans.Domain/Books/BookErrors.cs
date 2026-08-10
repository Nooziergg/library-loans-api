using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Books;

/// <summary>
/// Every failure the Book aggregate can produce, defined once.
///
/// This exists so that the same rule always reports the same code no matter which layer
/// detects it. The duplicate-ISBN rule is the case that proves the point: the application
/// layer checks for it before inserting, and the database catches the request that lost the
/// race a microsecond later. Those are two different code paths in two different projects, and
/// a client must not be able to tell them apart. Sharing one factory method is what makes that
/// true, rather than two string literals that agree until someone edits one of them.
/// </summary>
public static class BookErrors
{
    public static DomainError IsbnRequired() =>
        DomainError.Validation("book.isbn.required", "An ISBN is required.");

    public static DomainError IsbnMalformed(string input) =>
        DomainError.Validation(
            "book.isbn.malformed",
            $"'{Echo(input)}' is not an ISBN. Expected 10 or 13 digits, optionally hyphenated.");

    public static DomainError IsbnChecksumFailed(string input) =>
        DomainError.Validation(
            "book.isbn.checksum_failed",
            $"'{Echo(input)}' has the right shape for an ISBN but fails its check digit, so it is not a real ISBN.");

    /// <summary>
    /// Quoting the offending value back is what makes a validation message useful, but the
    /// value came from the caller and ends up in a response body and a log line. Truncating it
    /// keeps a rejected 30 MB request body from being echoed or written to disk.
    /// </summary>
    private static string Echo(string input)
    {
        if (input.Length <= MaxEchoedInputLength)
        {
            return input;
        }

        // Cutting at a fixed index can split a surrogate pair, leaving a lone surrogate that
        // serialises as a replacement character in the response body. Backing off one character
        // keeps the truncated text well-formed.
        var cut = char.IsHighSurrogate(input[MaxEchoedInputLength - 1])
            ? MaxEchoedInputLength - 1
            : MaxEchoedInputLength;

        return string.Concat(input.AsSpan(0, cut), "…");
    }

    private const int MaxEchoedInputLength = 20;

    public static DomainError DuplicateIsbn() =>
        DomainError.Conflict("book.isbn.duplicate", "A book with this ISBN is already in the catalogue.");

    public static DomainError TitleRequired() =>
        DomainError.Validation("book.title.required", "A title is required.");

    public static DomainError TitleTooLong() =>
        DomainError.Validation(
            "book.title.too_long",
            $"A title may be at most {Book.TitleMaxLength} characters.");

    public static DomainError AuthorRequired() =>
        DomainError.Validation("book.author.required", "An author is required.");

    public static DomainError AuthorTooLong() =>
        DomainError.Validation(
            "book.author.too_long",
            $"An author may be at most {Book.AuthorMaxLength} characters.");

    public static DomainError PublishedYearOutOfRange(int latestAllowed) =>
        DomainError.Validation(
            "book.published_year.out_of_range",
            $"A published year must be between {Book.EarliestPublishedYear} and {latestAllowed}.");

    /// <summary>
    /// Deletion refused because a copy is out. <b>Retryable</b> — the same request will succeed once
    /// the copy comes back, which is why it is a separate code from
    /// <see cref="HasLoanHistory"/>. Collapsing the two into one message would lose the only part a
    /// caller can act on.
    /// </summary>
    public static DomainError CopyOnLoan() =>
        DomainError.Conflict(
            "book.copy_on_loan",
            "A copy of this book is currently on loan. It can be deleted once every copy is back.");

    /// <summary>
    /// Deletion refused because the book has been borrowed at some point. <b>Not</b> retryable:
    /// lending history is a record, and removing the book would remove the loans that reference its
    /// copies. Withdrawing a title from circulation without erasing its history is a different
    /// operation, and one this system does not have yet.
    /// </summary>
    public static DomainError HasLoanHistory() =>
        DomainError.Conflict(
            "book.has_loan_history",
            "This book has lending history and cannot be deleted, because doing so would erase it.");

    /// <summary>
    /// Deletion lost a race with something adding a copy of the same title. Retryable, and rare
    /// enough that the honest message says what happened rather than pretending to be one of the
    /// other two.
    /// </summary>
    public static DomainError CopiesChangedDuringDelete() =>
        DomainError.Conflict(
            "book.copies_changed",
            "The copies of this book changed while it was being deleted. Try again.");

    public static DomainError NotFound(Guid id) =>
        DomainError.NotFound("book.not_found", $"No book exists with id {id}.");
}
