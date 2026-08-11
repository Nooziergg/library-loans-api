namespace LibraryLoans.Domain.Loans;

/// <summary>
/// The library's lending policy, in one place and named.
///
/// These are policy rather than physics: a different library would choose differently, and the
/// point of gathering them here is that changing one is a single edit with a single obvious blast
/// radius, rather than a hunt for the number 14 across the codebase.
/// </summary>
public static class LoanPolicy
{
    /// <summary>How long a borrower keeps a copy before it is due back.</summary>
    public const int LoanPeriodDays = 14;

    /// <summary>
    /// How many copies one member may hold at once.
    ///
    /// This limit is enforced in the aggregate and, unlike the one-active-loan-per-copy rule, is
    /// <b>not</b> backed by a database constraint: see the note on <c>Loan.Open</c> for why that
    /// asymmetry is deliberate.
    /// </summary>
    public const int MaxActiveLoansPerMember = 5;
}
