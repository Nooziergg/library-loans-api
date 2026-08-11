namespace LibraryLoans.Domain.Common;

/// <summary>
/// The outcome of an operation that either succeeds or fails for a domain reason, and produces no
/// value when it succeeds. Used by state transitions on an aggregate the caller already holds (
/// <c>Loan.Return</c>, <c>Member.Suspend</c>), where returning <c>Result&lt;Loan&gt;</c> would
/// wrongly suggest a new aggregate had been produced.
///
/// This duplicates about thirty lines of <see cref="Result{T}"/>, and that is the deliberate
/// choice. The obvious alternative is making <c>Result&lt;T&gt;</c> derive from this type, and a
/// reviewer will reasonably ask why it does not. The answer: both are sealed for the reason
/// documented below on <see cref="Result{T}"/> (no <c>default</c> instance, no representable
/// invalid state), and introducing an inheritance hierarchy into the most-returned type in the
/// system buys nothing except a base class that neither type ever uses polymorphically. Nothing
/// in this codebase accepts a <c>Result</c> and a <c>Result&lt;T&gt;</c> through one parameter.
/// Duplication of *code* is cheap here; the duplication worth avoiding is of *knowledge*, and
/// there is none: both types encode the same shape, not the same rule.
/// </summary>
public sealed class Result
{
    private readonly DomainError? _error;

    private Result()
    {
        _error = null;
        IsSuccess = true;
    }

    private Result(DomainError error)
    {
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    /// <summary>
    /// The failure. Non-nullable, so propagating it needs no null check. Throws on a successful
    /// result, because reading it there is a programming error rather than a runtime condition.
    /// </summary>
    public DomainError Error => IsSuccess
        ? throw new InvalidOperationException($"Cannot read {nameof(Error)} of a successful result.")
        : _error!;

    public static Result Success() => new();

    public static Result Failure(DomainError error) => new(error);

    /// <summary>
    /// Lets a guard clause read <c>return SomeError;</c>. Success has no such conversion, for the
    /// same reason as on <see cref="Result{T}"/>: constructing one should be visible.
    /// </summary>
    public static implicit operator Result(DomainError error) => new(error);
}

/// <summary>
/// The outcome of an operation that can fail for a domain reason: either a
/// <typeparamref name="T"/> or a <see cref="DomainError"/>, never both and never neither.
///
/// Deliberately a <c>class</c> and not a <c>readonly struct</c>. A struct is cheaper, but
/// <c>default(Result&lt;T&gt;)</c> would be a silently constructible instance that is neither a
/// success nor a real failure: a representable invalid state, in the one type every handler
/// in the system returns. In a codebase whose stated position is that invalid state should be
/// unrepresentable, that would be the wrong trade at any price. A private constructor plus a
/// reference type removes the possibility entirely.
///
/// Exceptions are reserved for the unexpected. A rejected ISBN is not unexpected. It is a
/// normal outcome of accepting input from the outside world, so it is a return value.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly DomainError? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(DomainError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    /// <summary>
    /// The produced value. Throws when the result is a failure: reading it without checking
    /// <see cref="IsSuccess"/> is a programming error, not a runtime condition to handle.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read {nameof(Value)} of a failed result. Error: {_error!.Code}.");

    /// <summary>
    /// The failure. Non-nullable by design: on the failure branch callers get a value the
    /// compiler already knows is present, so propagating an error needs no null check and no
    /// null-forgiving operator. Throws on a successful result, for the same reason
    /// <see cref="Value"/> throws on a failed one.
    /// </summary>
    public DomainError Error => IsSuccess
        ? throw new InvalidOperationException($"Cannot read {nameof(Error)} of a successful result.")
        : _error!;

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(DomainError error) => new(error);

    /// <summary>
    /// Lets a guard clause read <c>return SomeError;</c> instead of restating the type
    /// parameter. Success has no such conversion on purpose: constructing a success is
    /// something worth seeing at the call site.
    /// </summary>
    public static implicit operator Result<T>(DomainError error) => new(error);
}
