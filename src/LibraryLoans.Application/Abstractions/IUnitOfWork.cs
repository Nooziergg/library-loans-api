using LibraryLoans.Domain.Common;

namespace LibraryLoans.Application.Abstractions;

/// <summary>
/// Commits the work a handler has accumulated. Owned by Application, implemented by
/// Infrastructure: the dependency inversion that lets every handler be unit tested without a
/// database.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists pending changes.
    ///
    /// Returns a <see cref="Result{T}"/> rather than a bare row count because some failures at
    /// this point are domain outcomes, not faults. A unique-index violation means another
    /// request inserted the same thing first: a 409 the caller should see, not a 500. The
    /// implementation translates those, and lets everything else throw, because everything
    /// else genuinely is exceptional.
    ///
    /// The value carried on success is the number of rows affected, which is EF Core's own
    /// return; no placeholder type is invented to satisfy the generic.
    /// </summary>
    Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken);
}
