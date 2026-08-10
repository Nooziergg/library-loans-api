using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Books;

/// <summary>
/// Adds a title to the catalogue.
///
/// A plain class with a plain method, resolved from the container and called directly by the
/// endpoint. No mediator, no <c>IRequestHandler</c>, no runtime dispatch: the call site names
/// the type it invokes, so "find every caller of this use case" is a compiler question and the
/// stack trace in a log is the actual path the request took.
/// </summary>
public sealed class CreateBookHandler(
    IBookRepository books,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CreateBookHandler> logger)
{
    public async Task<Result<BookResponse>> HandleAsync(
        CreateBookCommand command,
        CancellationToken cancellationToken)
    {
        var isbn = Isbn.Create(command.Isbn);
        if (!isbn.IsSuccess)
        {
            return isbn.Error;
        }

        // Checked here so the ordinary case gets a clear, cheap rejection without a round trip
        // through a failed INSERT. This check alone is NOT what makes the rule true: two
        // requests can both pass it microseconds apart. The unique index is what survives that
        // race, and the unit of work translates its violation into the identical error below.
        if (await books.ExistsWithIsbnAsync(isbn.Value, cancellationToken))
        {
            return BookErrors.DuplicateIsbn();
        }

        var book = Book.Create(
            isbn.Value,
            command.Title,
            command.Author,
            command.PublishedYear,
            timeProvider.GetUtcNow());

        if (!book.IsSuccess)
        {
            return book.Error;
        }

        books.Add(book.Value);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        // A message template, not an interpolated string: the parameters stay separate fields
        // in the JSON payload, so a log search can filter on BookId rather than substring-match
        // a rendered sentence.
        logger.LogInformation(
            "Added book {BookId} to the catalogue with ISBN {Isbn}",
            book.Value.Id,
            book.Value.Isbn.Value);

        return Result<BookResponse>.Success(book.Value.ToResponse());
    }
}
