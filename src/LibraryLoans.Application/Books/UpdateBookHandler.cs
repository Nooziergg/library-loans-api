using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Books;

public sealed record UpdateBookCommand(Guid Id, string? Title, string? Author, int PublishedYear);

public sealed class UpdateBookHandler(
    IBookRepository books,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<UpdateBookHandler> logger)
{
    public async Task<Result<BookResponse>> HandleAsync(
        UpdateBookCommand command,
        CancellationToken cancellationToken)
    {
        // Tracked: a write that begins with a read.
        var book = await books.FindForUpdateAsync(command.Id, cancellationToken);
        if (book is null)
        {
            return BookErrors.NotFound(command.Id);
        }

        var updated = book.UpdateDetails(
            command.Title,
            command.Author,
            command.PublishedYear,
            timeProvider.GetUtcNow());

        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        logger.LogInformation("Updated book {BookId}", book.Id);

        return Result<BookResponse>.Success(book.ToResponse());
    }
}
