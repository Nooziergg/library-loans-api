using LibraryLoans.Application.Abstractions;
using LibraryLoans.Domain.Common;

namespace LibraryLoans.UnitTests.Fakes;

/// <summary>
/// Counts commits, and can be told to fail.
///
/// The count matters more than it looks. A handler that builds a valid aggregate, stages it and
/// then forgets to save returns a perfectly good 201 while writing nothing, and every
/// assertion about the returned response still passes. Only the commit count catches that, and
/// in this project the integration test that would otherwise catch it cannot run yet.
/// </summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    private readonly DomainError? _failWith;

    public RecordingUnitOfWork(DomainError? failWith = null) => _failWith = failWith;

    public int SaveCount { get; private set; }

    public Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.FromResult(_failWith is null
            ? Result<int>.Success(1)
            : Result<int>.Failure(_failWith));
    }
}
