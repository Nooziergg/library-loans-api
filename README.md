# Library Loans API

A CRUD API for a lending library, built to demonstrate Clean Architecture, enforced domain
invariants, and the cross-cutting concerns a production service needs.

## What is built, and what is not

This is a work in progress, and it is more useful to say so plainly than to let you find out by
calling an endpoint that does not exist.

**Built and verified end to end** — `docker compose up` on a clean machine yields a migrated,
working API:

- Clean Architecture across four projects, with the dependency rule enforced by a test rather
  than by convention
- **Every domain rule this system claims**, listed below, each enforced in the aggregate that owns
  it — and the critical one enforced again by the database
- Books, copies, members and loans: `POST /api/v1/books`, `GET /api/v1/books/{id}`,
  `POST /api/v1/books/{bookId}/copies`, `POST /api/v1/members`, `POST /api/v1/loans`,
  `POST /api/v1/loans/{id}/return`, `GET /api/v1/loans/{id}`
- The `Isbn` value object, checksum-validated and canonicalised so one book has one
  representation
- RFC 7807 for every failure; exception messages never reach a client
- EF Core on PostgreSQL, migrations committed and applied on startup
- An OpenAPI document at `/openapi/v1.json`
- 139 unit tests and 21 integration tests, the latter against a disposable PostgreSQL that
  Testcontainers creates and destroys per run

### The rules, and where each is enforced

| Rule | In the aggregate | Also in the database |
|---|---|---|
| A copy cannot be on two active loans at once | `Loan.Open` | **partial unique index** `(book_copy_id) WHERE returned_at IS NULL` |
| An ISBN must be structurally valid | `Isbn` | — |
| One ISBN appears once in the catalogue | pre-check | unique index |
| A barcode is unique across copies | pre-check | unique index |
| A member holds at most 5 active loans | `Loan.Open` | — (accepted race, see below) |
| A suspended member cannot borrow | `Loan.Open` | — |
| A loan cannot be returned twice | `Loan.Return` | — |
| A loan is due 14 days after it is taken | `Loan.Open` | check constraint |

**Not built yet:**

- Renewing a loan, retiring a copy, and reinstating a suspended member — each is a state
  transition whose guarding rule is deferred with it, on the principle that a guard whose
  precondition cannot be reached is a guard that cannot be tested
- Update and delete on books; list, filter, search and pagination
- Seed data — the database starts empty
- **Authentication and authorization — deliberately.** Every endpoint is anonymous. The brief
  does not ask for auth, and the budget went to the domain invariants it does ask for. The
  decision, the intended design (an external OIDC provider, default-deny, and why role rules and
  resource permissions belong in different layers) and the seams in code are in
  [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md)

**Deliberately out of scope**, and recorded as judgement rather than omission: distributed
caching, OpenTelemetry, CQRS read models, event sourcing, a microservice split, and an external
identity provider. Each is a real answer to a real scaling problem this system does not have, and
adding any of them to a CRUD service of this size would be harder to defend than leaving it out.

## Quick start

The only thing you need installed is **Docker** (or Podman).

```bash
git clone https://github.com/Nooziergg/library-loans-api.git
cd library-loans-api
docker compose up
```

Then:

```bash
curl http://localhost:8080/health/live          # -> Healthy
curl http://localhost:8080/openapi/v1.json      # every route, with schemas

# add a book, then read it back
curl -X POST http://localhost:8080/api/v1/books \
  -H 'Content-Type: application/json' \
  -d '{"isbn":"978-0-306-40615-7","title":"The Hobbit","author":"J. R. R. Tolkien","publishedYear":1937}'
```

The response carries a `Location` header; `curl` that to read the book back. Note the ISBN comes
back as `9780306406157` — hyphens stripped and, had you sent the 10-digit form, converted to its
13-digit equivalent, so one book has exactly one representation.

PowerShell:

```powershell
Invoke-RestMethod http://localhost:8080/health/live
Invoke-RestMethod http://localhost:8080/openapi/v1.json | ConvertTo-Json -Depth 4

$book = @{ isbn = '978-0-306-40615-7'; title = 'The Hobbit'
           author = 'J. R. R. Tolkien'; publishedYear = 1937 } | ConvertTo-Json
$created = Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/v1/books `
                             -ContentType 'application/json' -Body $book
Invoke-RestMethod "http://localhost:8080/api/v1/books/$($created.id)"
```

Things worth trying, because they show the rules rather than describing them:

```bash
# 422 - right shape, wrong check digit
curl -i -X POST http://localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"9780306406158","title":"x","author":"y","publishedYear":1990}'

# 409 - the ISBN-10 encoding of a book already in the catalogue
curl -i -X POST http://localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"0306406152","title":"x","author":"y","publishedYear":1990}'

# 400 - malformed request, rejected before the domain sees it
curl -i -X POST http://localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"9780306406157","publishedYear":1990}'
```

### Watching the loan rule work

The sequence worth running, because the last two steps are the whole argument. Substitute the ids
returned by each call:

```bash
# a title, a physical copy of it, and a borrower
curl -s -X POST localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"9780451524935","title":"Nineteen Eighty-Four","author":"George Orwell","publishedYear":1949}'
curl -s -X POST localhost:8080/api/v1/books/<bookId>/copies -H 'Content-Type: application/json' \
  -d '{"barcode":"COPY-0001"}'
curl -s -X POST localhost:8080/api/v1/members -H 'Content-Type: application/json' \
  -d '{"membershipNumber":"M00000001","name":"A Borrower","email":"borrower@example.test"}'

# borrow it
curl -s -X POST localhost:8080/api/v1/loans -H 'Content-Type: application/json' \
  -d '{"memberId":"<memberId>","bookCopyId":"<copyId>"}'

# borrow the same copy again -> 409, because it is already out
curl -i -X POST localhost:8080/api/v1/loans -H 'Content-Type: application/json' \
  -d '{"memberId":"<memberId>","bookCopyId":"<copyId>"}'

# give it back, then borrow it again -> 201, because the index is partial rather than plain
curl -s -X POST localhost:8080/api/v1/loans/<loanId>/return
curl -i -X POST localhost:8080/api/v1/loans -H 'Content-Type: application/json' \
  -d '{"memberId":"<memberId>","bookCopyId":"<copyId>"}'
```

The 409 and the 201 that follows it are the same index answering two different questions correctly.

To start over from an empty database:

```bash
docker compose down -v && docker compose up
```

## Running the tests

Unit tests need nothing running:

```bash
dotnet test tests/LibraryLoans.UnitTests
```

Integration tests boot the real HTTP pipeline against a real PostgreSQL instance that
Testcontainers starts, migrates and destroys per run — they never touch the development
database. Docker must be running; nothing else is needed:

```bash
dotnet test
```

## Solution layout

```
src/
  LibraryLoans.Domain          aggregates, value objects, invariants — zero package references
  LibraryLoans.Application     use cases and the port interfaces it owns
  LibraryLoans.Infrastructure  EF Core, migrations, seeding, port implementations
  LibraryLoans.Api             minimal APIs, middleware, composition root
tests/
  LibraryLoans.UnitTests       domain and application logic, the architecture rules, and the
                               API layer's pure functions (validation filter, error mapping,
                               exception handler) — no I/O, no database
  LibraryLoans.IntegrationTests real HTTP against a real database
```

Dependencies point inward only. That rule is not documentation — it is a test
(`DependencyRuleTests`) that walks the Domain assembly's transitive reference graph and
fails the build if EF Core or ASP.NET Core ever appears in it.

## Design notes

**Deliberate dependency choices.** A dependency is avoided here when it is *both* pervasive
and licence-unstable — pervasive meaning that removing it would touch every entity, DTO and
handler rather than one file. That rules out mapping and validation libraries, so mapping is
hand-written `ToResponse()` methods and validation lives in value objects that cannot hold
invalid state. Several well-known .NET libraries in exactly those two slots have moved to
commercial licensing after becoming load-bearing in thousands of codebases; the cost of
removing them scales with the size of the system.

Libraries whose blast radius is bounded are used freely — logging sits behind `ILogger`, so the
sink is a swap rather than a rewrite; Testcontainers never ships to production; seeding is a
single file. The distinction is the point: this is dependency *judgement*, not asceticism.

**Supply chain: the build refuses known-vulnerable packages.** Every version is pinned exactly in
`Directory.Packages.props` — no floating ranges, because a range resolves to whatever is newest at
restore time, which makes the build irreproducible and is the window a bad release arrives in.
NuGet audit runs over direct *and* transitive dependencies, and because MSBuild warnings are
errors here, a package with a published advisory fails the build rather than printing a warning
nobody reads.

That control has already paid for itself once. `Microsoft.AspNetCore.OpenApi` depends on
`Microsoft.OpenApi` 2.0.0, which is affected by CVE-2026-49451 — a circular schema reference
drives the parser into unbounded recursion and terminates the process, and a
`StackOverflowException` cannot be caught, so there is no runtime defence. The parent package has
not moved to the fixed version. Central package management with transitive pinning raises it to
2.11.0 without waiting: one line, no fork, no `PackageReference`. This service never parses
untrusted OpenAPI documents, so real exposure was near nil — it is pinned anyway, because *"we
weren't reachable by that one"* is a much worse answer to an auditor than *"we patched it"*.

**Why .NET 10 when the brief said .NET 9 or above.** .NET 9 is an STS release and left support on
12 May 2026. .NET 10 is LTS, supported to November 2028. Targeting a framework that is already out
of support is a decision you would have to defend later for no benefit now, and the cost here was
one line in `Directory.Build.props` because nothing in the code was framework-specific.

**Validation is hand-written, and .NET 10 ships an alternative.** Request-shape validation runs
through a small endpoint filter over DataAnnotations rather than .NET 10's in-box
`AddValidation()`. The honest comparison: the framework's version is fail-closed by construction,
because there is no per-endpoint attachment step to forget, whereas the filter here needs an
explicit throw to cover that case — which it has, and which is unit-tested. The filter was written
before the retarget to .NET 10 and kept because it is small, tested, and reused by every endpoint;
on a longer-lived codebase the first-party option is the better default.

**Two layers of validation, on purpose.** The request DTO enforces shape — required fields,
lengths, bounds that are compile-time constants — and produces `400`. The domain enforces every
rule unconditionally and produces `422`. The domain checks are therefore not redundant with the
DTO: they are what a non-HTTP caller such as a seeder or a message consumer gets, and they are
where rules that depend on runtime state live, like "a book cannot be published in the future".

**Uniqueness is enforced twice, and that is the part worth reading.** The application checks
before inserting, which gives the ordinary case a clean, cheap rejection. A unique index decides
the outcome when two requests check microseconds apart — and the constraint violation is
translated back into the *identical* domain error, matched on constraint name so an unrelated
collision is never reported as a duplicate. A client cannot tell which path rejected it. Enforcing
a rule only in application code leaves it true only most of the time.

**The loan index is *partial*, and that word is doing the work.**

```sql
CREATE UNIQUE INDEX ix_loans_active_copy ON loans (book_copy_id) WHERE returned_at IS NULL;
```

Without the `WHERE`, this reads "a copy may appear in the loans table at most once" — meaning a
returned book could never be borrowed again. With it, the constraint applies only to loans still
outstanding, which expresses a *temporal* invariant as a *static* index. That distinction is
invisible to almost every test one would think to write, so there is a test for exactly it:
`Borrows_the_same_copy_again_after_it_has_been_returned`, written before the migration existed.

It also settles a race the application cannot see: a return running concurrently with a re-borrow
of the same copy, where the new row cannot land while the old one still has a null `returned_at`.

And it pays twice. There is no availability or status column on a copy, because "on loan" is
derived state that a stored column could contradict — and the query that replaces it,
`NOT EXISTS (SELECT 1 FROM loans WHERE book_copy_id = c.id AND returned_at IS NULL)`, is served by
this same index. **The index that makes the invariant true is the index that makes the availability
query fast.**

**One race is accepted, deliberately.** Two concurrent borrows can both read a member's active-loan
count as four and both proceed, leaving them holding six. There is no constraint behind that limit,
and the reasoning is written at the guard: a member briefly over their limit is a policy annoyance a
librarian can unwind, while the same physical book promised to two people is a failure the library
cannot honour. Only the second is worth the cost of a database constraint. The same judgement
applies to a double return, where the outcome is idempotent in substance and nothing is promised
twice.

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers and the dependency rule, domain model, request flow, cross-cutting concerns. Marks clearly which parts are built. |
| [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) | **Not implemented.** Why, what would be used, and where the seams are |
| [docs/PREREQUISITES.md](docs/PREREQUISITES.md) | What to install — Docker only, to run it |

## License

MIT — see [LICENSE](LICENSE).
