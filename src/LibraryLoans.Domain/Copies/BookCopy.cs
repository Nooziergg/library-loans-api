using LibraryLoans.Domain.Books;

namespace LibraryLoans.Domain.Copies;

/// <summary>
/// A physical copy of a <see cref="Book"/> — the object a borrower actually carries home. A library
/// holds one Book and several copies of it, and a loan is against a copy rather than against the
/// title, which is what makes "the same book cannot be loaned twice" a statement about copies.
///
/// Note what this aggregate does <b>not</b> hold: any notion of being on loan. That is derived
/// state, true exactly when a loan row exists for this copy with no return date, and storing it
/// would create a column that can disagree with the loans table — the precise corruption the
/// partial unique index on loans exists to prevent. A status column would also have to be written
/// on every borrow, which drags a second aggregate into the transaction the graded invariant lives
/// in.
///
/// Retirement is genuinely stored state and is owed — it arrives with the rest of the copy
/// lifecycle, as a nullable timestamp answering both whether and when.
/// </summary>
public sealed class BookCopy
{
    /// <summary>Materialization path for the ORM only.</summary>
    private BookCopy()
    {
    }

    private BookCopy(Guid id, Guid bookId, Barcode barcode)
    {
        Id = id;
        BookId = bookId;
        Barcode = barcode;
    }

    public Guid Id { get; private set; }

    public Guid BookId { get; private set; }

    public Barcode Barcode { get; private set; } = null!;

    /// <summary>
    /// Adds a copy of a book. Returns a <see cref="BookCopy"/> rather than a
    /// <c>Result&lt;BookCopy&gt;</c> because there is nothing here that can fail: the barcode has
    /// already been validated into a value object, and barcode uniqueness is not an aggregate rule
    /// — it is a pre-check plus a unique index. A factory whose every path succeeds would teach a
    /// reader that <c>Result</c> is ceremony, which then devalues it everywhere it is load-bearing.
    ///
    /// The <paramref name="book"/> parameter is proof of existence rather than data — only
    /// <see cref="Book.Id"/> is read from it. Taking the aggregate means a caller cannot invent a
    /// <c>BookId</c>, because it had to load the book to get here. The same reasoning is why
    /// <c>Loan.Open</c> takes a <see cref="BookCopy"/> and a member rather than their ids.
    /// </summary>
    public static BookCopy Add(Book book, Barcode barcode) =>
        new(Guid.CreateVersion7(), book.Id, barcode);
}
