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
- The **book catalogue** as a complete vertical slice: `POST /api/v1/books`,
  `GET /api/v1/books/{id}`
- The `Isbn` value object, checksum-validated and canonicalised so one book has one
  representation
- Uniqueness enforced **twice** — in the application and by a database constraint, with the
  constraint violation translated back into the same domain error
- RFC 7807 for every failure; exception messages never reach a client
- EF Core on PostgreSQL, migration committed and applied on startup
- 69 unit tests and 9 integration tests, the latter against a disposable PostgreSQL that
  Testcontainers creates and destroys per run

**Not built yet:**

- `BookCopy`, `Member` and `Loan`, and therefore the loan rules — including "the same copy
  cannot be on two active loans at once". [The architecture doc](docs/ARCHITECTURE.md) specifies
  these and marks them clearly as unbuilt
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

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers and the dependency rule, domain model, request flow, cross-cutting concerns. Marks clearly which parts are built. |
| [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) | **Not implemented.** Why, what would be used, and where the seams are |
| [docs/PREREQUISITES.md](docs/PREREQUISITES.md) | What to install — Docker only, to run it |

## License

MIT — see [LICENSE](LICENSE).
