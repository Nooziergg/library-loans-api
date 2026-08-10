using System.ComponentModel.DataAnnotations;
using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;

namespace LibraryLoans.Api.Books;

/// <summary>
/// The wire shape for correcting a title's details.
///
/// <b>There is no ISBN field, deliberately.</b> The ISBN identifies the work — a row whose ISBN
/// changed is describing a different book, and the honest form of that operation is a delete and a
/// create. Accepting the field and rejecting a change would be the weaker version of this: it makes
/// the mistake possible and then complains about it, where leaving it out makes the mistake
/// unrepresentable. That is the same argument the value objects in this codebase make for
/// themselves.
/// </summary>
public sealed record UpdateBookRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(Book.TitleMaxLength)]
    public string? Title { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(Book.AuthorMaxLength)]
    public string? Author { get; init; }

    /// <summary>
    /// Only the lower bound is checked here; "not in the future" depends on the current time and so
    /// cannot be an attribute. The domain enforces it and answers 422.
    /// </summary>
    [Range(Book.EarliestPublishedYear, int.MaxValue)]
    public int PublishedYear { get; init; }

    public UpdateBookCommand ToCommand(Guid id) => new(id, Title, Author, PublishedYear);
}
