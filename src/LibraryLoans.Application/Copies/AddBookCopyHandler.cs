using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Books;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using LibraryLoans.Domain.Copies;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Copies;

public sealed class AddBookCopyHandler(
    IBookRepository books,
    IBookCopyRepository copies,
    IUnitOfWork unitOfWork,
    ILogger<AddBookCopyHandler> logger)
{
    public async Task<Result<BookCopyResponse>> HandleAsync(
        AddBookCopyCommand command,
        CancellationToken cancellationToken)
    {
        var barcode = Barcode.Create(command.Barcode);
        if (!barcode.IsSuccess)
        {
            return barcode.Error;
        }

        // Loaded rather than trusted. BookCopy.Add takes the aggregate as proof the title exists, so
        // a caller cannot attach a copy to a BookId it invented — the foreign key would catch that
        // too, but as a 500 rather than a described 404.
        var book = await books.GetByIdAsync(command.BookId, cancellationToken);
        if (book is null)
        {
            return BookErrors.NotFound(command.BookId);
        }

        if (await copies.ExistsWithBarcodeAsync(barcode.Value, cancellationToken))
        {
            return BookCopyErrors.DuplicateBarcode();
        }

        var copy = BookCopy.Add(book, barcode.Value);
        copies.Add(copy);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        logger.LogInformation(
            "Added copy {BookCopyId} of book {BookId} with barcode {Barcode}",
            copy.Id,
            book.Id,
            copy.Barcode.Value);

        return Result<BookCopyResponse>.Success(copy.ToResponse());
    }
}
