using LibraryLoans.Domain.Copies;

namespace LibraryLoans.Application.Copies;

/// <summary>Positional, for the reason given on <c>BookResponse</c>.</summary>
public sealed record BookCopyResponse(Guid Id, Guid BookId, string Barcode);

public static class BookCopyMappings
{
    public static BookCopyResponse ToResponse(this BookCopy copy) =>
        new(copy.Id, copy.BookId, copy.Barcode.Value);
}

public sealed record AddBookCopyCommand(Guid BookId, string? Barcode);
