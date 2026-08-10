using LibraryLoans.Domain.Common;

namespace LibraryLoans.UnitTests.Common;

/// <summary>
/// <see cref="Result{T}"/> is the return type of every handler in the system, so its edges are
/// worth pinning down explicitly rather than discovering through a handler test that fails for
/// an unrelated reason.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void A_success_carries_its_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        var error = DomainError.Conflict("thing.duplicate", "Already exists.");

        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    /// <summary>
    /// Reading the value of a failure is a bug in the caller, and it should behave like one.
    /// Returning <c>default</c> instead would let a rejected operation continue with a zero or a
    /// null and fail somewhere else entirely.
    /// </summary>
    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result<string>.Failure(DomainError.NotFound("thing.not_found", "Gone."));

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("thing.not_found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_the_error_of_a_success_throws()
    {
        var result = Result<string>.Success("fine");

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_domain_error_converts_implicitly_to_a_failure()
    {
        var error = DomainError.Validation("thing.invalid", "No.");

        Result<string> result = error;

        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }
}
