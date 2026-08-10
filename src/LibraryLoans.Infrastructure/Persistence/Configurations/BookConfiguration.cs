using LibraryLoans.Domain.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryLoans.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Book"/> to the <c>books</c> table.
///
/// Table and column names are written out rather than left to EF Core's default PascalCase.
/// PostgreSQL folds unquoted identifiers to lower case, so a <c>PublishedYear</c> column has to
/// be double-quoted in every hand-written query forever; <c>published_year</c> does not. The
/// project documentation invites a reviewer to open psql and look around, and this is what
/// makes that pleasant.
/// </summary>
internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("books");

        builder.HasKey(book => book.Id)
            .HasName(DatabaseConstraints.BooksPrimaryKey);

        builder.Property(book => book.Id)
            .HasColumnName("id")
            // Ids are version-7 GUIDs assigned by the aggregate, so the database must not
            // try to generate one.
            .ValueGeneratedNever();

        builder.Property(book => book.Isbn)
            .HasColumnName("isbn")
            .HasMaxLength(Isbn.Length)
            .IsRequired()
            .HasConversion(
                isbn => isbn.Value,
                value => Isbn.FromPersistedValue(value),
                // Isbn is a record, so Equals is already value equality; stating the comparer
                // explicitly means the change tracker cannot fall back to reference equality
                // and report an unchanged book as modified.
                new ValueComparer<Isbn>(
                    (left, right) => left!.Value == right!.Value,
                    isbn => isbn.Value.GetHashCode(),
                    isbn => Isbn.FromPersistedValue(isbn.Value)));

        // The rule that one ISBN appears once in the catalogue, enforced where it cannot be
        // raced. The application checks first to give a clean error in the ordinary case; this
        // index is what decides the outcome when two requests check simultaneously, and the
        // unit of work turns its violation into the same 409 the pre-check would have produced.
        builder.HasIndex(book => book.Isbn)
            .IsUnique()
            .HasDatabaseName(DatabaseConstraints.BooksIsbnUniqueIndex);

        builder.Property(book => book.Title)
            .HasColumnName("title")
            .HasMaxLength(Book.TitleMaxLength)
            .IsRequired();

        builder.Property(book => book.Author)
            .HasColumnName("author")
            .HasMaxLength(Book.AuthorMaxLength)
            .IsRequired();

        builder.Property(book => book.PublishedYear)
            .HasColumnName("published_year")
            .IsRequired();
    }
}
