namespace LibraryLoans.UnitTests.Fakes;

/// <summary>
/// A clock that does not move.
///
/// Hand-written rather than taken from a package: <see cref="TimeProvider"/> is an abstract
/// class with one member this codebase uses, so a fake is four lines. A dependency would be
/// larger than the thing it replaces.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
