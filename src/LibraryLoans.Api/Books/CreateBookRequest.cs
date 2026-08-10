using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;

// The property below is also called Isbn, and inside this type the property wins. The alias
// keeps the attribute pointing at the domain constant rather than silently failing to compile
// into something else.
using DomainIsbn = LibraryLoans.Domain.Books.Isbn;

namespace LibraryLoans.Api.Books;

/// <summary>
/// The wire shape for adding a book.
///
/// The limits below are the domain's own constants, not copies of them. If
/// <see cref="Book.TitleMaxLength"/> changes, the 400 and the 422 change together; duplicated
/// literals would drift and leave the API rejecting at one length while the domain rejects at
/// another.
/// </summary>
public sealed record CreateBookRequest
{
    /// <summary>
    /// The length bound is defence in depth, not the actual guard. <see cref="Isbn.Create"/>
    /// enforces it itself, because it is a public domain entry point and a seeder or a message
    /// consumer would never pass through these attributes. Rejecting an absurd value here as a
    /// 400 simply keeps it from travelling any further than it must.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(DomainIsbn.MaxInputLength)]
    public string? Isbn { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(Book.TitleMaxLength)]
    public string? Title { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(Book.AuthorMaxLength)]
    public string? Author { get; init; }

    /// <summary>
    /// Only the lower bound is checked here. The upper bound is "not in the future", which
    /// depends on the current time and therefore cannot be a compile-time constant in an
    /// attribute — the domain enforces it against an injected clock and returns 422. That
    /// division is not an accident of the framework; it is the reason the domain check is not
    /// redundant with this one.
    /// </summary>
    [Range(Book.EarliestPublishedYear, int.MaxValue)]
    public int PublishedYear { get; init; }

    public CreateBookCommand ToCommand() => new(Isbn, Title, Author, PublishedYear);
}
