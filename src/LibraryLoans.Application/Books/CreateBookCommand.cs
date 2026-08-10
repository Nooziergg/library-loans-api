namespace LibraryLoans.Application.Books;

/// <summary>
/// A request to add a title to the catalogue.
///
/// The string properties are nullable because this crosses the trust boundary: the caller may
/// send anything, and the handler's job is to turn "anything" into either a valid aggregate or
/// a described failure. Declaring them non-nullable here would be a claim the type cannot
/// actually keep.
/// </summary>
public sealed record CreateBookCommand(string? Isbn, string? Title, string? Author, int PublishedYear);
