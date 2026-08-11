namespace LibraryLoans.Domain.Common;

/// <summary>
/// The category of a domain failure. This is the seam that keeps HTTP out of the Domain:
/// the domain states what <i>kind</i> of thing went wrong, and exactly one file in the API
/// layer decides which status code that becomes.
/// </summary>
public enum DomainErrorKind
{
    /// <summary>Input could not form a valid domain concept. Caller sent something wrong.</summary>
    Validation,

    /// <summary>Input was well-formed but a business rule forbids the operation.</summary>
    RuleViolation,

    /// <summary>The operation collided with existing state, typically a uniqueness rule.</summary>
    Conflict,

    /// <summary>The addressed thing does not exist.</summary>
    NotFound,
}

/// <summary>
/// A failure carrying a stable machine-readable <paramref name="Code"/> and a
/// human-readable <paramref name="Message"/>.
///
/// The code is part of the API contract and reaches clients as the ProblemDetails
/// <c>code</c> extension; the message is not, and may be reworded at any time. Clients that
/// branch on wording rather than code are the reason that distinction is drawn here rather
/// than left implicit.
/// </summary>
public sealed record DomainError(string Code, string Message, DomainErrorKind Kind)
{
    public static DomainError Validation(string code, string message) =>
        new(code, message, DomainErrorKind.Validation);

    public static DomainError RuleViolation(string code, string message) =>
        new(code, message, DomainErrorKind.RuleViolation);

    public static DomainError Conflict(string code, string message) =>
        new(code, message, DomainErrorKind.Conflict);

    public static DomainError NotFound(string code, string message) =>
        new(code, message, DomainErrorKind.NotFound);
}
