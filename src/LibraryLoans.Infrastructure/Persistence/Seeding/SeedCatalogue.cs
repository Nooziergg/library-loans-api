namespace LibraryLoans.Infrastructure.Persistence.Seeding;

/// <summary>
/// The catalogue the seeder builds, and the borrowers who use it.
///
/// Titles and authors are real, because a catalogue of generated lorem is filler a reviewer scrolls
/// past, while <c>?search=orwell</c> returning <i>Nineteen Eighty-Four</i> demonstrates something.
///
/// <b>The ISBNs are not real.</b> They are structurally valid (correct ISBN-13 check digits, so the
/// domain accepts them), but they are generated rather than the identifiers these editions actually
/// carry. That is deliberate: attaching a genuine ISBN to a row invented for a demonstration would
/// put a real identifier on the wrong record, and the value of the seed is in the titles being
/// recognisable, not in the numbers being lookupable. See <c>IsbnFor</c>.
///
/// <b>Natural keys are deliberately distinct from the ones the test suite uses.</b> Seeded ISBNs
/// begin 9781, barcodes are <c>LIB-*</c> and membership numbers <c>M9*</c>; the integration tests
/// arrange with 9780 ISBNs, <c>COPY-*</c> barcodes and <c>M0*</c> members. Without that separation a
/// test that seeds and then posts its own fixture data collides on a unique index, and the failure
/// points at the test rather than at the overlap.
/// </summary>
internal static class SeedCatalogue
{
    /// <summary>Sixty titles. The count matters only in that the brief asks for 100 rows overall.</summary>
    internal static readonly (string Title, string Author, int PublishedYear)[] Books =
    [
        ("Nineteen Eighty-Four", "George Orwell", 1949),
        ("Animal Farm", "George Orwell", 1945),
        ("Homage to Catalonia", "George Orwell", 1938),
        ("The Hobbit", "J. R. R. Tolkien", 1937),
        ("The Fellowship of the Ring", "J. R. R. Tolkien", 1954),
        ("The Two Towers", "J. R. R. Tolkien", 1954),
        ("The Return of the King", "J. R. R. Tolkien", 1955),
        ("Pride and Prejudice", "Jane Austen", 1813),
        ("Sense and Sensibility", "Jane Austen", 1811),
        ("Emma", "Jane Austen", 1815),
        ("Persuasion", "Jane Austen", 1817),
        ("Great Expectations", "Charles Dickens", 1861),
        ("Bleak House", "Charles Dickens", 1853),
        ("A Tale of Two Cities", "Charles Dickens", 1859),
        ("David Copperfield", "Charles Dickens", 1850),
        ("Middlemarch", "George Eliot", 1872),
        ("Silas Marner", "George Eliot", 1861),
        ("Jane Eyre", "Charlotte Bronte", 1847),
        ("Wuthering Heights", "Emily Bronte", 1847),
        ("Frankenstein", "Mary Shelley", 1818),
        ("Dracula", "Bram Stoker", 1897),
        ("The Picture of Dorian Gray", "Oscar Wilde", 1890),
        ("Heart of Darkness", "Joseph Conrad", 1899),
        ("Lord Jim", "Joseph Conrad", 1900),
        ("Mrs Dalloway", "Virginia Woolf", 1925),
        ("To the Lighthouse", "Virginia Woolf", 1927),
        ("Orlando", "Virginia Woolf", 1928),
        ("Brave New World", "Aldous Huxley", 1932),
        ("The Great Gatsby", "F. Scott Fitzgerald", 1925),
        ("Tender Is the Night", "F. Scott Fitzgerald", 1934),
        ("The Sun Also Rises", "Ernest Hemingway", 1926),
        ("A Farewell to Arms", "Ernest Hemingway", 1929),
        ("The Old Man and the Sea", "Ernest Hemingway", 1952),
        ("Of Mice and Men", "John Steinbeck", 1937),
        ("The Grapes of Wrath", "John Steinbeck", 1939),
        ("East of Eden", "John Steinbeck", 1952),
        ("To Kill a Mockingbird", "Harper Lee", 1960),
        ("The Catcher in the Rye", "J. D. Salinger", 1951),
        ("Fahrenheit 451", "Ray Bradbury", 1953),
        ("The Martian Chronicles", "Ray Bradbury", 1950),
        ("Dune", "Frank Herbert", 1965),
        ("Foundation", "Isaac Asimov", 1951),
        ("I, Robot", "Isaac Asimov", 1950),
        ("The Left Hand of Darkness", "Ursula K. Le Guin", 1969),
        ("A Wizard of Earthsea", "Ursula K. Le Guin", 1968),
        ("The Dispossessed", "Ursula K. Le Guin", 1974),
        ("Slaughterhouse-Five", "Kurt Vonnegut", 1969),
        ("Cat's Cradle", "Kurt Vonnegut", 1963),
        ("Beloved", "Toni Morrison", 1987),
        ("Song of Solomon", "Toni Morrison", 1977),
        ("Things Fall Apart", "Chinua Achebe", 1958),
        ("One Hundred Years of Solitude", "Gabriel Garcia Marquez", 1967),
        ("Love in the Time of Cholera", "Gabriel Garcia Marquez", 1985),
        ("The Remains of the Day", "Kazuo Ishiguro", 1989),
        ("Never Let Me Go", "Kazuo Ishiguro", 2005),
        ("Wolf Hall", "Hilary Mantel", 2009),
        ("Bring Up the Bodies", "Hilary Mantel", 2012),
        ("The Handmaid's Tale", "Margaret Atwood", 1985),
        ("Oryx and Crake", "Margaret Atwood", 2003),
        ("Norwegian Wood", "Haruki Murakami", 1987),
    ];

    /// <summary>Forty borrowers. Names are a fixed list rather than generated: see the seeder's note.</summary>
    internal static readonly (string Name, string Email)[] Members =
    [
        ("Alice Whitfield", "alice.whitfield@example.test"),
        ("Bruno Castellani", "bruno.castellani@example.test"),
        ("Chidi Okonkwo", "chidi.okonkwo@example.test"),
        ("Dagny Solberg", "dagny.solberg@example.test"),
        ("Elena Vasquez", "elena.vasquez@example.test"),
        ("Farid Haddad", "farid.haddad@example.test"),
        ("Greta Lindqvist", "greta.lindqvist@example.test"),
        ("Hiroshi Tanaka", "hiroshi.tanaka@example.test"),
        ("Ines Moreau", "ines.moreau@example.test"),
        ("Jonas Bergstrom", "jonas.bergstrom@example.test"),
        ("Kavita Raman", "kavita.raman@example.test"),
        ("Liam O'Sullivan", "liam.osullivan@example.test"),
        ("Marta Kowalski", "marta.kowalski@example.test"),
        ("Nikolai Petrov", "nikolai.petrov@example.test"),
        ("Olamide Adeyemi", "olamide.adeyemi@example.test"),
        ("Priya Chandrasekaran", "priya.chandra@example.test"),
        ("Quentin Dubois", "quentin.dubois@example.test"),
        ("Rosa Delgado", "rosa.delgado@example.test"),
        ("Samuel Nkemelu", "samuel.nkemelu@example.test"),
        ("Tomas Novak", "tomas.novak@example.test"),
        ("Ursula Brandt", "ursula.brandt@example.test"),
        ("Viktor Almeida", "viktor.almeida@example.test"),
        ("Wilhelmina Ross", "wilhelmina.ross@example.test"),
        ("Xiulan Chen", "xiulan.chen@example.test"),
        ("Yusuf Demir", "yusuf.demir@example.test"),
        ("Zofia Wojcik", "zofia.wojcik@example.test"),
        ("Adaeze Nwosu", "adaeze.nwosu@example.test"),
        ("Benedikt Hauser", "benedikt.hauser@example.test"),
        ("Camille Rousseau", "camille.rousseau@example.test"),
        ("Dmitri Sokolov", "dmitri.sokolov@example.test"),
        ("Eleni Papadaki", "eleni.papadaki@example.test"),
        ("Fabio Bianchi", "fabio.bianchi@example.test"),
        ("Gabriela Santos", "gabriela.santos@example.test"),
        ("Henrik Dahl", "henrik.dahl@example.test"),
        ("Isabel Ferreira", "isabel.ferreira@example.test"),
        ("Jakub Zielinski", "jakub.zielinski@example.test"),
        ("Karin Andersen", "karin.andersen@example.test"),
        ("Lucas Mwangi", "lucas.mwangi@example.test"),
        ("Mei Lin Ong", "meilin.ong@example.test"),
        ("Noor Al-Rashid", "noor.alrashid@example.test"),
    ];

    /// <summary>
    /// A structurally valid ISBN-13 derived from an index: same input, same output, on every
    /// machine and every run.
    ///
    /// The <c>9781</c> prefix keeps seeded rows clear of the <c>9780</c> range the integration tests
    /// arrange with, so a test that seeds and then posts its own book cannot collide on the unique
    /// index.
    /// </summary>
    internal static string IsbnFor(int index)
    {
        var body = $"9781{index:D8}";

        var checksum = 0;
        for (var position = 0; position < body.Length; position++)
        {
            checksum += (body[position] - '0') * (position % 2 == 0 ? 1 : 3);
        }

        return body + (10 - (checksum % 10)) % 10;
    }

    internal static string BarcodeFor(int index) => $"LIB-{index:D5}";

    /// <summary>
    /// <c>M</c> followed by eight digits, which is the format the value object enforces. The leading
    /// <c>9</c> is what keeps seeded members clear of the <c>M0...</c> range the integration tests use.
    /// </summary>
    internal static string MembershipNumberFor(int index) => $"M9{index:D7}";
}
