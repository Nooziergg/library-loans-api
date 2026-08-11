using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Members;

public enum MemberStatus
{
    Active,
    Suspended,
}

/// <summary>
/// A borrower.
///
/// Note what a Member is not: a login. Identity would come from an external provider, so this
/// aggregate holds no credentials and there is no user table — see <c>docs/AUTHORIZATION.md</c>.
/// </summary>
public sealed class Member
{
    public const int NameMaxLength = 150;

    /// <summary>The maximum length of an email address, per RFC 5321's practical limit.</summary>
    public const int EmailMaxLength = 254;

    /// <summary>
    /// Width of the status column. The status is stored as text rather than as the enum's underlying
    /// integer so the schema reads without a lookup, which means it needs a width — and that width
    /// belongs here beside the values it has to accommodate, not as a bare number in a mapping file.
    /// A future status longer than this would be SQLSTATE 22001 and a 500.
    /// </summary>
    public const int StatusMaxLength = 16;

    /// <summary>Materialization path for the ORM only.</summary>
    private Member()
    {
    }

    private Member(Guid id, MembershipNumber membershipNumber, string name, string email, MemberStatus status)
    {
        Id = id;
        MembershipNumber = membershipNumber;
        Name = name;
        Email = email;
        Status = status;
    }

    public Guid Id { get; private set; }

    // `= null!` for the reason given on Book: every construction path assigns these, and so does
    // the ORM when reading a row, but the compiler cannot see either.
    public MembershipNumber MembershipNumber { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public MemberStatus Status { get; private set; }

    /// <summary>
    /// Derived, never stored. Explicitly ignored in the EF configuration — a <c>can_borrow</c>
    /// column would be a second source of truth for something the status already answers.
    /// </summary>
    public bool CanBorrow => Status is MemberStatus.Active;

    /// <summary>
    /// The only way to bring a Member into existence. New members are Active; there is no way to
    /// register one already suspended, because suspension is something that happens to a member
    /// rather than a property they are created with.
    /// </summary>
    public static Result<Member> Register(MembershipNumber membershipNumber, string? name, string? email)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return MemberErrors.NameRequired();
        }

        if (trimmedName.Length > NameMaxLength)
        {
            return MemberErrors.NameTooLong();
        }

        var trimmedEmail = email?.Trim();
        if (string.IsNullOrEmpty(trimmedEmail))
        {
            return MemberErrors.EmailRequired();
        }

        if (trimmedEmail.Length > EmailMaxLength)
        {
            return MemberErrors.EmailTooLong();
        }

        // Shape only, and deliberately shallow. Whether an address can receive mail is answered by
        // sending mail to it, not by a pattern — so this rejects what is obviously not an address
        // and declines to pretend it can do more. An RFC 5322 parser here would be a lot of code
        // buying a stronger claim than the type can actually make.
        if (!IsPlausibleEmail(trimmedEmail))
        {
            return MemberErrors.EmailMalformed();
        }

        return Result<Member>.Success(new Member(
            Guid.CreateVersion7(),
            membershipNumber,
            trimmedName,
            trimmedEmail,
            MemberStatus.Active));
    }

    /// <summary>
    /// Suspends the member, blocking new borrowing. Existing loans are untouched: suspension stops
    /// someone taking more books out, it does not recall what they already have.
    ///
    /// Suspending an already-suspended member is a <see cref="DomainErrorKind.Conflict"/> rather
    /// than a no-op, matching the ruling on returning a loan twice. The caller asked for a state
    /// change that did not happen, and saying so is more useful than silence.
    ///
    /// Reached from <c>POST /api/v1/members/{id}/suspend</c>, and from the data seeder, which needs
    /// a suspended member so that the rule preventing them from borrowing is visible to a reviewer
    /// rather than merely tested. It is the only door into the Suspended state, which is what makes
    /// the guard on borrowing while suspended testable at all.
    /// </summary>
    public Result Suspend()
    {
        if (Status is MemberStatus.Suspended)
        {
            return MemberErrors.AlreadySuspended();
        }

        Status = MemberStatus.Suspended;

        return Result.Success();
    }

    private static bool IsPlausibleEmail(string value)
    {
        var atIndex = value.IndexOf('@', StringComparison.Ordinal);

        // Exactly one '@', with something before it and a dotted something after it.
        return atIndex > 0
               && atIndex == value.LastIndexOf('@')
               && atIndex < value.Length - 1
               && value.IndexOf('.', atIndex) > atIndex + 1
               && !value.EndsWith('.')
               && !value.Contains(' ', StringComparison.Ordinal);
    }
}
