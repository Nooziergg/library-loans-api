using LibraryLoans.Application.Abstractions;
using LibraryLoans.Application.Copies;
using LibraryLoans.Application.Loans;
using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Common;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Application.Books;

/// <summary>
/// Removes a title and its physical copies.
///
/// Two preconditions, reported separately because a caller can act on one and not the other: a copy
/// currently out means "try again when it is back", while any lending history at all means "never":
/// deleting the book would remove the copies that a loan record points at, and lending history is a
/// record rather than a cache.
///
/// The copies are removed explicitly rather than by cascade. The foreign keys are <c>Restrict</c>,
/// so the database refuses to delete rows as a side effect of deleting others; that is deliberate,
/// and the cost of it is exactly this handler naming what it removes.
///
/// There is no explicit transaction, and there must not be: the connection is configured with a
/// retrying execution strategy, which refuses a user-initiated transaction unless it is wrapped in
/// its own <c>ExecuteAsync</c>. One <c>SaveChangesAsync</c> is already a single transaction, and EF
/// orders the copies before the book from the model's relationships.
/// </summary>
public sealed class DeleteBookHandler(
    IBookRepository books,
    IBookCopyRepository copies,
    ILoanRepository loans,
    IUnitOfWork unitOfWork,
    ILogger<DeleteBookHandler> logger)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var book = await books.FindForUpdateAsync(id, cancellationToken);
        if (book is null)
        {
            return BookErrors.NotFound(id);
        }

        if (await loans.HasActiveLoanForBookAsync(id, cancellationToken))
        {
            return BookErrors.CopyOnLoan();
        }

        if (await loans.HasAnyLoanForBookAsync(id, cancellationToken))
        {
            return BookErrors.HasLoanHistory();
        }

        var copiesOfBook = await copies.FindAllForBookForUpdateAsync(id, cancellationToken);

        copies.RemoveRange(copiesOfBook);
        books.Remove(book);

        // If a borrow landed between the checks above and this line, the foreign key rejects the
        // delete and the unit of work turns that into the same retryable conflict the first check
        // produces. The checks give a clear answer in the ordinary case; the constraint is what
        // makes the rule true under concurrency.
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error;
        }

        logger.LogInformation(
            "Deleted book {BookId} and its {CopyCount} copies",
            id,
            copiesOfBook.Count);

        return Result.Success();
    }
}
