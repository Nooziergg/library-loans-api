using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Members;

/// <summary>
/// Every failure the Member aggregate can produce, defined once, for the reason
/// <see cref="Books.BookErrors"/> states: the duplicate-membership-number rule is detected in two
/// different projects (the application's pre-check, and the database's unique index catching the
/// request that lost a race), and a client must not be able to tell them apart.
/// </summary>
public static class MemberErrors
{
    private const int MaxEchoedInputLength = 20;

    public static DomainError MembershipNumberRequired() =>
        DomainError.Validation("member.membership_number.required", "A membership number is required.");

    public static DomainError MembershipNumberMalformed(string input) =>
        DomainError.Validation(
            "member.membership_number.malformed",
            $"'{Echo(input)}' is not a membership number. Expected 'M' followed by eight digits.");

    public static DomainError DuplicateMembershipNumber() =>
        DomainError.Conflict(
            "member.membership_number.duplicate",
            "A member with this membership number already exists.");

    public static DomainError NameRequired() =>
        DomainError.Validation("member.name.required", "A name is required.");

    public static DomainError NameTooLong() =>
        DomainError.Validation(
            "member.name.too_long",
            $"A name may be at most {Member.NameMaxLength} characters.");

    public static DomainError EmailRequired() =>
        DomainError.Validation("member.email.required", "An email address is required.");

    public static DomainError EmailTooLong() =>
        DomainError.Validation(
            "member.email.too_long",
            $"An email address may be at most {Member.EmailMaxLength} characters.");

    public static DomainError EmailMalformed() =>
        DomainError.Validation(
            "member.email.malformed",
            "That does not look like an email address.");

    /// <summary>
    /// Suspending an already-suspended member is a conflict, not a silent no-op: the same ruling
    /// this domain applies to returning a loan twice. An operation that quietly does nothing is
    /// indistinguishable, from the caller's side, from one that worked.
    /// </summary>
    public static DomainError AlreadySuspended() =>
        DomainError.Conflict("member.already_suspended", "This member is already suspended.");

    public static DomainError NotFound(Guid id) =>
        DomainError.NotFound("member.not_found", $"No member exists with id {id}.");

    /// <summary>
    /// Bounded, for the reason given on <see cref="Books.BookErrors"/>: the value came from the
    /// caller and ends up in a response body and a log line.
    /// </summary>
    private static string Echo(string input) =>
        input.Length <= MaxEchoedInputLength
            ? input
            : string.Concat(input.AsSpan(0, MaxEchoedInputLength), "...");
}
