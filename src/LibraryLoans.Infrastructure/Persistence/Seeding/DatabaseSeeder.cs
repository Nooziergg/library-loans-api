using LibraryLoans.Domain.Books;
using LibraryLoans.Domain.Copies;
using LibraryLoans.Domain.Loans;
using LibraryLoans.Domain.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibraryLoans.Infrastructure.Persistence.Seeding;

/// <summary>
/// Fills an empty database with a working library: sixty titles, a hundred and fifty physical
/// copies, forty borrowers and eighty loans.
///
/// <para><b>Everything is built through the domain factories.</b> <c>Book.Create</c>,
/// <c>Member.Register</c>, <c>BookCopy.Add</c> and <c>Loan.Open</c> — never by constructing rows
/// directly. Two things follow from that, and the second is the interesting one. The seeded data
/// provably satisfies every invariant, because it was produced by the same code that refuses
/// invalid data over HTTP. And the seeder becomes the only caller of the domain that is not a web
/// request, which is what would catch an aggregate that only works when driven from an endpoint —
/// it has to supply <c>memberActiveLoanCount</c> and <c>copyHasActiveLoan</c> itself, from what it
/// is building, which is the proof that <c>Loan.Open</c>'s signature is usable off the HTTP
/// path.</para>
///
/// <para><b>Deterministic, with two stated limits.</b> Nothing here is random — no faker library, no
/// seeded <c>Random</c>, just index arithmetic over fixed lists — so the same titles, barcodes and
/// membership numbers appear on every machine. Ids are <i>not</i> reproducible, because they are
/// version-7 GUIDs containing a timestamp; neither are the dates, which are computed from the
/// current instant at boot. Tests therefore assert on natural keys and on relative properties
/// ("this loan is overdue"), never on ids or absolute dates.</para>
///
/// <para><b>One <c>SaveChangesAsync</c> for the whole seed</b>, so it is a single transaction. A
/// seeder that committed in stages could crash halfway and leave a database that the emptiness
/// check below reads as already seeded — permanently half-populated, with nothing to indicate
/// it.</para>
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>The five states a reviewer should be able to observe without editing any data.</summary>
    private const int AtLoanLimitMemberIndex = 0;
    private const int SuspendedMemberIndex = 1;
    private const int OverdueMemberIndex = 2;
    /// <summary>
    /// Index 3 rather than 0, because copies-per-title is <c>index % 4 + 1</c> — so this title has
    /// four copies and "every copy is out" is a demonstration rather than a technicality about a
    /// title that only ever had one.
    /// </summary>
    private const int FullyBorrowedBookIndex = 3;

    public static async Task SeedAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSeeder));

        // Idempotent by emptiness, so `docker compose restart` never duplicates anything. Checking
        // one table is enough: everything is written in a single transaction, so books exist if and
        // only if the whole seed does.
        if (await dbContext.Books.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Database already contains data; skipping seed");
            return;
        }

        var now = timeProvider.GetUtcNow();

        var books = BuildBooks(now);
        var copies = BuildCopies(books);
        var members = BuildMembers();
        var loans = BuildLoans(books, copies, members, now);

        dbContext.Books.AddRange(books);
        dbContext.BookCopies.AddRange(copies.SelectMany(entry => entry.Value));
        dbContext.Members.AddRange(members);
        dbContext.Loans.AddRange(loans);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {BookCount} books, {CopyCount} copies, {MemberCount} members and {LoanCount} loans",
            books.Count,
            copies.Sum(entry => entry.Value.Count),
            members.Count,
            loans.Count);
    }

    private static List<Book> BuildBooks(DateTimeOffset now)
    {
        var books = new List<Book>(SeedCatalogue.Books.Length);

        for (var index = 0; index < SeedCatalogue.Books.Length; index++)
        {
            var (title, author, publishedYear) = SeedCatalogue.Books[index];

            var isbn = Isbn.Create(SeedCatalogue.IsbnFor(index));
            if (!isbn.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Seed ISBN {index} was rejected: {isbn.Error.Code}. Check IsbnFor's check digit.");
            }

            var book = Book.Create(isbn.Value, title, author, publishedYear, now);

            // Checked rather than assumed. Reading .Value on a failure throws a message with no
            // index in it — "book.title.too_long" and nothing about which of sixty titles. The seed
            // data is static, so this is unreachable unless someone edits it badly, which is exactly
            // when a message naming the row is worth having.
            if (!book.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Seed book {index} ('{title}') was rejected by the domain: {book.Error.Code}.");
            }

            books.Add(book.Value);
        }

        return books;
    }

    /// <summary>
    /// One to four copies per title, by index rather than by chance, so the distribution is the same
    /// everywhere. Sixty titles yields a hundred and fifty copies.
    /// </summary>
    private static Dictionary<Guid, List<BookCopy>> BuildCopies(List<Book> books)
    {
        var copies = new Dictionary<Guid, List<BookCopy>>(books.Count);
        var barcodeNumber = 0;

        for (var index = 0; index < books.Count; index++)
        {
            var forThisBook = new List<BookCopy>();

            for (var copyNumber = 0; copyNumber <= index % 4; copyNumber++)
            {
                var barcode = Barcode.Create(SeedCatalogue.BarcodeFor(barcodeNumber++));
                if (!barcode.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Seed barcode {barcodeNumber - 1} was rejected: {barcode.Error.Code}.");
                }

                forThisBook.Add(BookCopy.Add(books[index], barcode.Value));
            }

            copies[books[index].Id] = forThisBook;
        }

        return copies;
    }

    private static List<Member> BuildMembers()
    {
        var members = new List<Member>(SeedCatalogue.Members.Length);

        for (var index = 0; index < SeedCatalogue.Members.Length; index++)
        {
            var (name, email) = SeedCatalogue.Members[index];

            var membershipNumber = MembershipNumber.Create(SeedCatalogue.MembershipNumberFor(index));
            if (!membershipNumber.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Seed membership number {index} was rejected: {membershipNumber.Error.Code}.");
            }

            var member = Member.Register(membershipNumber.Value, name, email);
            if (!member.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Seed member {index} ('{name}') was rejected by the domain: {member.Error.Code}.");
            }

            members.Add(member.Value);
        }

        // The state that makes invariant 5 observable: this borrower authenticates fine and is
        // refused at the point of borrowing, which is a rule firing rather than a rule described.
        members[SuspendedMemberIndex].Suspend();

        return members;
    }

    /// <summary>
    /// Opens the loans, arranged so that each rule the system enforces is visible from the outside.
    ///
    /// The active-loan bookkeeping is done here in memory precisely because <c>Loan.Open</c> refuses
    /// to guess: it takes the member's active count and whether the copy is out as arguments, and
    /// this method has to supply both truthfully. A bug in that bookkeeping would produce a seed the
    /// domain rejects, which is a better failure than a seed that quietly violates an invariant.
    /// </summary>
    private static List<Loan> BuildLoans(
        List<Book> books,
        Dictionary<Guid, List<BookCopy>> copies,
        List<Member> members,
        DateTimeOffset now)
    {
        var loans = new List<Loan>();
        var activeLoanCount = new int[members.Count];
        var copyIsOut = new HashSet<Guid>();

        var allCopies = books.SelectMany(book => copies[book.Id]).ToList();

        void Open(BookCopy copy, int memberIndex, DateTimeOffset loanedAt, bool returnIt)
        {
            var member = members[memberIndex];

            var loan = Loan.Open(
                copy,
                member,
                memberActiveLoanCount: activeLoanCount[memberIndex],
                copyHasActiveLoan: copyIsOut.Contains(copy.Id),
                now: loanedAt);

            if (!loan.IsSuccess)
            {
                // Unreachable if the bookkeeping above is right, and worth failing loudly rather
                // than silently seeding fewer rows than intended.
                throw new InvalidOperationException(
                    $"Seed produced a loan the domain refused: {loan.Error.Code}. The active-loan " +
                    "bookkeeping in the seeder is wrong.");
            }

            if (returnIt)
            {
                loan.Value.Return(loanedAt.AddDays(7));
            }
            else
            {
                activeLoanCount[memberIndex]++;
                copyIsOut.Add(copy.Id);
            }

            loans.Add(loan.Value);
        }

        // Every copy of one title is out, so `?availableOnly=true` visibly excludes it while a plain
        // search still finds it.
        var fullyBorrowed = copies[books[FullyBorrowedBookIndex].Id];
        for (var index = 0; index < fullyBorrowed.Count; index++)
        {
            Open(fullyBorrowed[index], memberIndex: 10 + index, now.AddDays(-3), returnIt: false);
        }

        // One borrower holding the maximum, so the next borrow is refused.
        var atLimit = allCopies.Where(copy => !copyIsOut.Contains(copy.Id)).Take(5).ToList();
        foreach (var copy in atLimit)
        {
            Open(copy, AtLoanLimitMemberIndex, now.AddDays(-10), returnIt: false);
        }

        // One loan already past its due date, so `?overdue=true` returns something.
        var overdueCopy = allCopies.First(copy => !copyIsOut.Contains(copy.Id));
        Open(overdueCopy, OverdueMemberIndex, now.AddDays(-30), returnIt: false);

        // History: loans taken and given back. These are what make the catalogue look used, and they
        // are also what would break a plain unique index on book_copy_id — the copies below are
        // borrowable again, which is the partial index doing its job.
        var returnedCandidates = allCopies.Where(copy => !copyIsOut.Contains(copy.Id)).Take(30).ToList();
        for (var index = 0; index < returnedCandidates.Count; index++)
        {
            // Spread across the borrowers whose state is not deliberately arranged, so the
            // at-limit, suspended and overdue members stay the only demonstrations of their rules.
            // A returned loan does not count against anyone's limit, so no bookkeeping is needed.
            var memberIndex = 3 + (index % (members.Count - 3));

            Open(returnedCandidates[index], memberIndex, now.AddDays(-60 + index), returnIt: true);
        }

        // The rest: ordinary loans in progress, spread thinly so nobody but member 0 is near the cap.
        foreach (var copy in allCopies.Where(copy => !copyIsOut.Contains(copy.Id)))
        {
            if (loans.Count >= 80)
            {
                break;
            }

            var memberIndex = NextEligibleMember(activeLoanCount, members.Count);
            if (memberIndex is null)
            {
                break;
            }

            Open(copy, memberIndex.Value, now.AddDays(-(loans.Count % 12)), returnIt: false);
        }

        return loans;
    }

    /// <summary>
    /// The next borrower who is neither suspended, nor the deliberately-at-limit member, nor already
    /// holding enough that one more would reach the cap. Keeping ordinary members below the limit is
    /// what leaves the at-limit member as the only one demonstrating that rule.
    /// </summary>
    private static int? NextEligibleMember(int[] activeLoanCount, int memberCount)
    {
        for (var index = 3; index < memberCount; index++)
        {
            if (activeLoanCount[index] < LoanPolicy.MaxActiveLoansPerMember - 1)
            {
                return index;
            }
        }

        return null;
    }
}
