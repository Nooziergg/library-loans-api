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

    // How many loans a member may hold at once used to live here too, and it moved to
    // Member.MaxActiveLoans. It reads as a lending policy, but it is a rule about a member rather
    // than about a loan, and keeping it here meant Loan.Open evaluated a member's eligibility
    // using a constant from its own namespace. The day that limit varies by member category or by
    // branch, it is Member that grows the lookup.
}
