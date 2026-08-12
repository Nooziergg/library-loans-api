# Library Loans API

A CRUD API for a lending library, built to demonstrate Clean Architecture, enforced domain
invariants, and the cross-cutting concerns a production service needs.

`docker compose up` on a clean machine gives you a migrated, seeded, working API. Nothing else to
install, on macOS or Windows.

## The rule this project is built around

A physical copy cannot be on two active loans at once. It is enforced twice, and the second time is
the interesting one:

```sql
CREATE UNIQUE INDEX ix_loans_active_copy
    ON loans (book_copy_id)
    WHERE returned_at IS NULL;
```

`Loan.Open(...)` is the only way to construct a loan and refuses when the copy is already out. That
handles the ordinary case and gives a clean error. But between the check and the insert another
request can pass the same check, so the partial index decides the race, and the violation is
translated back into the identical domain error rather than surfacing as a 500.

The filter is the whole point. A plain unique index on `book_copy_id` would mean a copy could be
borrowed once in its entire life and never again, which is a different and much worse rule that
happens to pass most of the same tests. It is a temporal invariant expressed as a static index, and
it is a large part of why this runs on PostgreSQL.

### Every rule, and where each is enforced

| Rule | In the aggregate | Also in the database |
|---|---|---|
| A copy cannot be on two active loans at once | `Loan.Open` | **partial unique index** `(book_copy_id) WHERE returned_at IS NULL` |
| An ISBN must be structurally valid | `Isbn` | not applicable |
| One ISBN appears once in the catalogue | pre-check | unique index |
| A barcode is unique across copies | pre-check | unique index |
| A member holds at most 5 active loans | `Loan.Open` | not enforced (accepted race, see below) |
| A suspended member cannot borrow | `Loan.Open` | not applicable |
| A loan cannot be returned twice | `Loan.Return` | not applicable |
| A loan is due 14 days after it is taken | `Loan.Open` | not applicable, the period is policy rather than a schema rule |
| A loan's due date is after its loan date | `Loan.Open`, by construction | check constraint `due_at > loaned_at` |

The five-loan limit is deliberately enforced once. A member briefly holding six is a policy
annoyance a librarian can unwind; the same physical book promised to two people is a failure the
library cannot honour. Accepting a race creates a debt to detect it, and the reconciliation query
that pays that debt is in [ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Quick start

The only thing you need installed is **Docker** (or Podman).

```bash
git clone https://github.com/Nooziergg/library-loans-api.git
cd library-loans-api
docker compose up
```

That is the whole setup. The database comes up **already populated** (60 titles, 150 physical
copies, 40 borrowers and 80 loans), so there is something to look at immediately:

```bash
curl http://localhost:8080/health/live          # -> Healthy
curl http://localhost:8080/openapi/v1.json      # every route, with schemas

curl -s "localhost:8080/api/v1/books?search=orwell"                  # three titles
curl -s "localhost:8080/api/v1/books?pageSize=1" | grep totalCount    # 60 in the catalogue
curl -s "localhost:8080/api/v1/books?availableOnly=true&pageSize=1"   # 40: the rest are all on loan

curl -s "localhost:8080/api/v1/loans?overdue=true"                   # somebody is late
curl -s "localhost:8080/api/v1/members?status=Suspended"             # and somebody is suspended
```

PowerShell:

```powershell
Invoke-RestMethod http://localhost:8080/health/live
Invoke-RestMethod "http://localhost:8080/api/v1/books?search=orwell" | Select-Object -Expand items
Invoke-RestMethod "http://localhost:8080/api/v1/books?availableOnly=true&pageSize=1" |
    Select-Object totalCount
```

**[docs/WALKTHROUGH.md](docs/WALKTHROUGH.md) is where the rules are shown rather than described**:
borrowing the same copy twice and watching the index refuse it, the two distinct voices of 400 and
422, the audit trail recording who changed what, and a retried `POST` replaying its original
response. Every number on that page came from running the command.

## What is built

- Clean Architecture across four projects, with the dependency rule enforced by a test rather than
  by convention
- Sixteen endpoints over books, copies, members and loans, including full CRUD on the catalogue,
  where `DELETE /books/{id}` refuses to erase lending history
- **Search and filtering**: `search` over title, author and ISBN; `availableOnly`; loans filtered by
  borrower, active state and overdue; members by status. Paging with a capped page size, sorting
  restricted to a published allowlist
- The `Isbn` value object, checksum-validated and canonicalised so one book has one representation
- RFC 7807 for every failure; exception messages never reach a client
- EF Core on PostgreSQL, migrations committed and applied on startup, OpenAPI at `/openapi/v1.json`
- **Structured logs**: JSON on stdout, one entry per request via the framework's `UseHttpLogging`,
  and an identifier returned in `X-Correlation-Id` that also appears in every error body
- **An audit trail**: every insert, update and delete recorded with the actor, the correlation id
  and the before and after values, written inside the same transaction as the change it describes
- **Idempotent retries**: `POST` accepts an `Idempotency-Key`, and a repeat of the same request
  replays the original response instead of doing the work twice
- **Seed data, 330 rows out of the box**, arranged so the rules are visible rather than described
- 183 unit tests and 120 integration tests, the latter against a disposable PostgreSQL that
  Testcontainers creates and destroys per run

One deliberate shape worth flagging here rather than in a footnote: the member register returns
**identifiers and status only, no names or email addresses**. While authorization does not exist, a
paged collection is bulk extraction of the whole membership, and it is enumeration rather than
secrecy that turns a missing auth layer into a data breach. Reading one member by id needs a GUID
you already have, and keeps the full detail.

## What is not built

- **Authentication and authorization, left out deliberately.** Every endpoint is anonymous. The
  brief does not ask for auth and the budget went to the invariants it does ask for. The decision,
  the intended design and the seams in code are in [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md)
- **Renewing a loan, retiring a copy, reinstating a suspended member.** Each is a state transition
  whose guarding rule is deferred with it, on the principle that a guard whose precondition cannot
  be reached is a guard that cannot be tested
- **Update and delete on members and copies.** The catalogue carries the full set because that is
  where the interesting rule lives. Elsewhere the same verbs are the same shape a third time with no
  new rule to show, and *removing* a member or a copy is really the retirement transition above
- **Expiry for the audit trail and the idempotency keys.** Both tables only grow, and both are
  indexed on their timestamp so the job that deletes past a cutoff is a range scan. It belongs in a
  scheduled job, not as a timer inside a web process that may be running on three replicas

**Deliberately out of scope**, recorded as judgement rather than omission: distributed caching,
OpenTelemetry, CQRS read models, event sourcing, and a microservice split. Each answers a real
scaling problem this system does not have, and the thresholds at which they start to pay are in
[docs/DECISIONS.md](docs/DECISIONS.md).

## Running the tests

Unit tests need nothing running:

```bash
dotnet test tests/LibraryLoans.UnitTests
```

Integration tests boot the real HTTP pipeline against a real PostgreSQL instance that
Testcontainers starts, migrates and destroys per run: they never touch the development
database. Docker must be running; nothing else is needed:

```bash
dotnet test
```

## Solution layout

```
src/
  LibraryLoans.Domain          aggregates, value objects, invariants, zero package references
  LibraryLoans.Application     use cases and the port interfaces it owns
  LibraryLoans.Infrastructure  EF Core, migrations, seeding, port implementations
  LibraryLoans.Api             minimal APIs, middleware, composition root
tests/
  LibraryLoans.UnitTests       domain and application logic, the architecture rules, and the
                               API layer's pure functions (validation filter, error mapping,
                               exception handler). No I/O, no database
  LibraryLoans.IntegrationTests real HTTP against a real database
```

Dependencies point inward only. That rule is not documentation. It is a test
(`DependencyRuleTests`) that walks the Domain assembly's transitive reference graph and
fails the build if EF Core or ASP.NET Core ever appears in it.

## Documentation

| Document | Contents |
|---|---|
| [docs/WALKTHROUGH.md](docs/WALKTHROUGH.md) | The rules demonstrated against a running container: the loan invariant, the two kinds of refusal, the seed, the log line, the audit trail, idempotent retries |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers and the dependency rule, domain model, request flow, cross-cutting concerns. Marks clearly which parts are built |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Why the dependencies were chosen and refused, the two layers of validation, .NET 10, and what would change at scale |
| [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) | **Not implemented.** Why, what would be used, and where the seams are |
| [docs/PREREQUISITES.md](docs/PREREQUISITES.md) | What to install: Docker only, to run it |

The same pages are on the [wiki](https://github.com/Nooziergg/library-loans-api/wiki) for reading in
a browser. The copies in `docs/` are the source of truth: they are versioned with the code they
describe, and they come down with a clone.

## License

MIT: see [LICENSE](LICENSE).
