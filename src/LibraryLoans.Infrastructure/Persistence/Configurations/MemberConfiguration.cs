using LibraryLoans.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Persistence.Configurations;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members");

        builder.HasKey(member => member.Id)
            .HasName(DatabaseConstraints.MembersPrimaryKey);

        builder.Property(member => member.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(member => member.MembershipNumber)
            .HasColumnName("membership_number")
            .HasMaxLength(MembershipNumber.Length)
            .IsRequired()
            .HasConversion(
                membershipNumber => membershipNumber.Value,
                value => MembershipNumber.FromPersistedValue(value),
                new ValueComparer<MembershipNumber>(
                    (left, right) => left!.Value == right!.Value,
                    membershipNumber => membershipNumber.Value.GetHashCode(),
                    membershipNumber => MembershipNumber.FromPersistedValue(membershipNumber.Value)));

        builder.HasIndex(member => member.MembershipNumber)
            .IsUnique()
            .HasDatabaseName(DatabaseConstraints.MembersMembershipNumberUniqueIndex);

        builder.Property(member => member.Name)
            .HasColumnName("name")
            .HasMaxLength(Member.NameMaxLength)
            .IsRequired();

        builder.Property(member => member.Email)
            .HasColumnName("email")
            .HasMaxLength(Member.EmailMaxLength)
            .IsRequired();

        // Stored as text, not as the enum's underlying int. The schema is meant to be readable in
        // psql without a trip into source to learn that 1 means Suspended.
        builder.Property(member => member.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        // Derived from Status; see the equivalent note in LoanConfiguration.
        builder.Ignore(member => member.CanBorrow);
    }
}
