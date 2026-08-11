using LibraryLoans.Domain.Common;

namespace LibraryLoans.Domain.Loans;

/// <summary>
/// Every failure the Loan aggregate can produce, defined once.
///
/// <see cref="CopyAlreadyOnLoan"/> is the reason this pattern exists at all. That rule is checked
/// in the application before inserting, and enforced again by a partial unique index that catches
/// whichever concurrent request lost the race. Those are two different code paths in two different
/// projects, and a client must not be able to tell which one rejected it. One factory method is
/// what makes that true, rather than two string literals that agree until somebody edits one.
/// </summary>
public static class LoanErrors
{
    /// <summary>
    /// A <see cref="DomainErrorKind.Conflict"/>, so it becomes a 409, from both the in-memory
    /// check and the database's ruling. It is a collision with existing state, not a defect in the
    /// request: the same request would have succeeded a moment earlier or a moment later.
    /// </summary>
    public static DomainError CopyAlreadyOnLoan() =>
        DomainError.Conflict("loan.copy.already_on_loan", "This copy is already on loan.");

    public static DomainError MemberSuspended() =>
        DomainError.RuleViolation(
            "loan.member.suspended",
            "This member is suspended and cannot borrow.");

    public static DomainError MemberAtLoanLimit() =>
        DomainError.RuleViolation(
            "loan.member.at_loan_limit",
            $"This member already holds the maximum of {LoanPolicy.MaxActiveLoansPerMember} active loans.");

    /// <summary>
    /// A conflict rather than a rule violation, and for the same reason as
    /// <see cref="CopyAlreadyOnLoan"/>: returning a loan twice collides with the state the resource
    /// is already in. It is deliberately not a silent success: a caller that believes it returned
    /// a book it did not return has been misled, and this is the only signal that says otherwise.
    /// </summary>
    public static DomainError AlreadyReturned() =>
        DomainError.Conflict("loan.already_returned", "This loan has already been returned.");

    public static DomainError NotFound(Guid id) =>
        DomainError.NotFound("loan.not_found", $"No loan exists with id {id}.");
}
