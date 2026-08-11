using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Loan"/>, and with it the single most important line in this schema: the partial
/// unique index that makes "a copy cannot be on two active loans" true under concurrency rather than
/// merely true most of the time.
/// </summary>
internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    /// <summary>
    /// The column name is needed twice: once to map the property, once inside the raw SQL of the
    /// index filter, which the compiler cannot check against the model. A constant means a rename
    /// cannot leave the filter pointing at a column that no longer exists.
    /// </summary>
    private const string ReturnedAtColumn = "returned_at";
    private const string LoanedAtColumn = "loaned_at";
    private const string DueAtColumn = "due_at";

    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable(
            "loans",
            // Raw SQL again, so the column names come from the same constants the mapping uses:
            // the compiler cannot check either string against the model.
            table => table.HasCheckConstraint(
                DatabaseConstraints.LoansDueAfterLoanedCheck,
                $"{DueAtColumn} > {LoanedAtColumn}"));

        builder.HasKey(loan => loan.Id)
            .HasName(DatabaseConstraints.LoansPrimaryKey);

        builder.Property(loan => loan.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(loan => loan.BookCopyId)
            .HasColumnName("book_copy_id")
            .IsRequired();

        builder.Property(loan => loan.MemberId)
            .HasColumnName("member_id")
            .IsRequired();

        builder.Property(loan => loan.LoanedAt)
            .HasColumnName(LoanedAtColumn)
            .IsRequired();

        builder.Property(loan => loan.DueAt)
            .HasColumnName(DueAtColumn)
            .IsRequired();

        // Nullable, and that null is the definition of "active" throughout the system.
        builder.Property(loan => loan.ReturnedAt)
            .HasColumnName(ReturnedAtColumn);

        // Derived from ReturnedAt. Ignored explicitly rather than relying on EF's conventions for
        // get-only properties, because an is_active column sitting beside returned_at would be a
        // second answer to a question the first column already settles: precisely the stored
        // derived state this design argues against. The generated migration is read to confirm it.
        builder.Ignore(loan => loan.IsActive);

        // -------------------------------------------------------------------------------------
        //  The invariant, enforced where it cannot be raced.
        //
        //  The filter is not an optimisation. It is the rule. Without it this reads
        //  "a copy may appear in the loans table at most once", meaning a returned book could
        //  never be borrowed again. With it, the constraint applies only to rows that represent
        //  a loan still outstanding, which is a *temporal* invariant expressed as a *static*
        //  index. PostgreSQL supports this directly, and it is a large part of why this project
        //  is on PostgreSQL.
        //
        //  It also arbitrates a race the application cannot: a return running concurrently with a
        //  re-borrow of the same copy. The new row cannot land while the old one still has a null
        //  returned_at, whichever order the two statements arrive in.
        // -------------------------------------------------------------------------------------
        builder.HasIndex(loan => loan.BookCopyId)
            .IsUnique()
            .HasFilter($"{ReturnedAtColumn} IS NULL")
            .HasDatabaseName(DatabaseConstraints.LoansActiveCopyUniqueIndex);

        // Same technique, different purpose: this one is not unique and exists so the active-loan
        // count on the borrow path is an index seek rather than a scan that grows with history.
        builder.HasIndex(loan => loan.MemberId)
            .HasFilter($"{ReturnedAtColumn} IS NULL")
            .HasDatabaseName(DatabaseConstraints.LoansMemberActiveIndex);

        // Relationships without navigation properties: Loan holds ids by design, and configuring
        // them this way is what keeps that true. Declaring HasOne(l => l.BookCopy) would require
        // adding the navigation, which invites loading a graph on every read.
        //
        // Restrict, not the Cascade that EF Core defaults required references to. Deleting a member
        // must not silently erase their loan history. That is a records question before it is a
        // referential-integrity one, and the answer a librarian expects is a refusal.
        builder.HasOne<BookCopy>()
            .WithMany()
            .HasForeignKey(loan => loan.BookCopyId)
            .HasConstraintName(DatabaseConstraints.LoansBookCopyForeignKey)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Member>()
            .WithMany()
            .HasForeignKey(loan => loan.MemberId)
            .HasConstraintName(DatabaseConstraints.LoansMemberForeignKey)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
