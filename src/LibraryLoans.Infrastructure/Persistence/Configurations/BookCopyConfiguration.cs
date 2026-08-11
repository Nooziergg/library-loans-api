using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Persistence.Configurations;

internal sealed class BookCopyConfiguration : IEntityTypeConfiguration<BookCopy>
{
    public void Configure(EntityTypeBuilder<BookCopy> builder)
    {
        builder.ToTable("book_copies");

        builder.HasKey(copy => copy.Id)
            .HasName(DatabaseConstraints.BookCopiesPrimaryKey);

        builder.Property(copy => copy.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(copy => copy.BookId)
            .HasColumnName("book_id")
            .IsRequired();

        builder.Property(copy => copy.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(Barcode.MaxLength)
            .IsRequired()
            .HasConversion(
                barcode => barcode.Value,
                value => Barcode.FromPersistedValue(value),
                new ValueComparer<Barcode>(
                    (left, right) => left!.Value == right!.Value,
                    barcode => barcode.Value.GetHashCode(),
                    barcode => Barcode.FromPersistedValue(barcode.Value)));

        builder.HasIndex(copy => copy.Barcode)
            .IsUnique()
            .HasDatabaseName(DatabaseConstraints.BookCopiesBarcodeUniqueIndex);

        builder.HasIndex(copy => copy.BookId)
            .HasDatabaseName(DatabaseConstraints.BookCopiesBookIndex);

        // Restrict rather than Cascade: deleting a title must not silently take its physical copies
        //, and their loan history, with it. The rule a library wants is a refusal.
        builder.HasOne<Book>()
            .WithMany()
            .HasForeignKey(copy => copy.BookId)
            .HasConstraintName(DatabaseConstraints.BookCopiesBookForeignKey)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
