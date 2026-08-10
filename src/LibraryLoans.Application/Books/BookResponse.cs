using LibraryLoans.Domain.Books;

namespace LibraryLoans.Application.Books;

/// <summary>
/// The public shape of a book.
///
/// Positional on purpose. Two places build this record — the mapping below, and the SQL
/// projection in the read adapter — and a positional record makes adding a field a compile
/// error in both. A settable-property DTO would let the two drift, with the projection quietly
/// leaving the new field at its default.
/// </summary>
public sealed record BookResponse(Guid Id, string Isbn, string Title, string Author, int PublishedYear);

/// <summary>
/// Hand-written mapping. No reflection, no configuration to keep in sync, no startup
/// validation step: if the shapes stop matching, the build says so.
/// </summary>
public static class BookMappings
{
    public static BookResponse ToResponse(this Book book) =>
        new(book.Id, book.Isbn.Value, book.Title, book.Author, book.PublishedYear);
}
