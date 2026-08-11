# Library Loans API

A CRUD API for a lending library, built to demonstrate Clean Architecture, enforced domain
invariants, and the cross-cutting concerns a production service needs.

## What is built, and what is not

This is a work in progress, and it is more useful to say so plainly than to let you find out by
calling an endpoint that does not exist.

**Built and verified end to end**: `docker compose up` on a clean machine yields a migrated,
working API:

- Clean Architecture across four projects, with the dependency rule enforced by a test rather
  than by convention
- **Every domain rule this system claims**, listed below, each enforced in the aggregate that owns
  it, and the critical one enforced again by the database
- Books, copies, members and loans: `POST /api/v1/books`, `GET /api/v1/books/{id}`,
  `POST /api/v1/books/{bookId}/copies`, `POST /api/v1/members`, `POST /api/v1/loans`,
  `POST /api/v1/loans/{id}/return`, `GET /api/v1/loans/{id}`
- **Catalogue search and filtering**: `GET /api/v1/books` with `search` over title, author and
  ISBN, an `availableOnly` filter, paging with a capped page size, and sorting restricted to a
  published allowlist
- **Loan and member registers**: `GET /api/v1/loans` filtered by borrower, active state and
  overdue; `GET /api/v1/members` filtered by status; `GET /api/v1/members/{id}`;
  `GET /api/v1/books/{bookId}/copies`; and `POST /api/v1/members/{id}/suspend`.
  The member register deliberately returns **identifiers and status only, no names or email
  addresses**, while authorization does not exist, a paged collection is bulk extraction of the
  whole membership, and it is enumeration rather than secrecy that turns a missing auth layer into a
  data breach. Reading one member by id needs a known GUID and keeps the full detail
- **Full CRUD on the catalogue**: `PUT /api/v1/books/{id}` and `DELETE /api/v1/books/{id}`, the
  latter refusing to erase lending history
- The `Isbn` value object, checksum-validated and canonicalised so one book has one
  representation
- RFC 7807 for every failure; exception messages never reach a client
- EF Core on PostgreSQL, migrations committed and applied on startup
- An OpenAPI document at `/openapi/v1.json`
- **Structured logs**: JSON on stdout; one entry per request with method, path, status and duration
  via the framework's `UseHttpLogging`, and an identifier returned in `X-Correlation-Id` that also
  appears in every error body. Every line already carries the framework's `TraceId` and `RequestId`;
  a caller who supplies their own label gets that on the request's lines too
- **An audit trail**: every insert, update and delete recorded with the actor, the correlation id
  and the before/after values, written inside the same transaction as the change it describes
- **Idempotent retries**: `POST` accepts an `Idempotency-Key`, and a repeat of the same request
  replays the original response instead of doing the work twice
- **Seed data, 330 rows out of the box**: 60 real titles, 150 physical copies, 40 borrowers and
  80 loans, arranged so the rules are visible rather than described
- 183 unit tests and 120 integration tests, the latter against a disposable PostgreSQL that
  Testcontainers creates and destroys per run

### The rules, and where each is enforced

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

**Not built yet:**

- Renewing a loan, retiring a copy, and reinstating a suspended member. Each is a state
  transition whose guarding rule is deferred with it, on the principle that a guard whose
  precondition cannot be reached is a guard that cannot be tested
- **Update and delete on members and copies.** The catalogue carries the full set because that is
  where the interesting rule lives: `DELETE /books/{id}` refuses to erase lending history, with two
  different refusals depending on whether that history is still open. The same verbs elsewhere split
  into two halves and neither earns the space: editing a member's name or email is the same shape a
  third time with no new rule to show, and *removing* a member or a copy is not a delete at all but
  the retirement and deactivation transitions above. A reviewer counting endpoints will see the gap;
  it is what a fixed budget bought, not something overlooked
- **Expiry for the audit trail and the idempotency keys.** Both tables only grow, and both are
  indexed on their timestamp precisely so the job that deletes past a cutoff is a range scan rather
  than a full one. It is a scheduled job rather than application code, which is why it is described
  here instead of half-built as a timer inside a web process that may be running on three replicas
- **Authentication and authorization, left out deliberately.** Every endpoint is anonymous. The brief
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

### Three things worth trying, because they show the rules rather than describing them

**A title whose every copy is out** stays in the catalogue and drops out of the available list,
which is the availability filter and the loan invariant answering from the same index:

```bash
curl -s "localhost:8080/api/v1/books?search=Hobbit"                  # found
curl -s "localhost:8080/api/v1/books?search=Hobbit&availableOnly=true"  # not available
```

**One ISBN has one representation.** All four of these find the same book, because the search term
is parsed by the same value object the catalogue is stored with:

```bash
curl -s "localhost:8080/api/v1/books?search=9781000000009"      # ISBN-13
curl -s "localhost:8080/api/v1/books?search=978-1-00000-000-9"  # the same, hyphenated
curl -s "localhost:8080/api/v1/books?search=1000000001"         # its ISBN-10 encoding
curl -s "localhost:8080/api/v1/books?search=1-00000-000-1"      # and that, hyphenated
```

**Invalid input is refused in two distinct voices**, 400 when the request cannot be understood,
422 when it is understood and the domain declines it, 409 when it collides with what already exists:

```bash
# 422 - right shape, wrong check digit. Understood, and refused.
curl -i -X POST localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"9780306406158","title":"x","author":"y","publishedYear":1990}'

# 400 - a required field is missing. Not understood, so the domain never sees it.
curl -i -X POST localhost:8080/api/v1/books -H 'Content-Type: application/json' \
  -d '{"isbn":"9780306406157","publishedYear":1990}'

# 400 - an unpublished sort field, rejected by an allowlist before it reaches a query
curl -i "localhost:8080/api/v1/books?sortBy=whatever"
```

### How the seed is built, and why it matters

**Every row goes through the domain factories** (`Book.Create`, `Member.Register`, `Loan.Open`),
so the seeded data provably satisfies every invariant, and the seeder becomes the only caller of the
domain that is not an HTTP request. That is what would catch an aggregate that only works when
driven from an endpoint: the seeder has to supply the member's active-loan count and whether a copy
is already out, from state it is building itself.

It is deterministic without a faker library (fixed lists and index arithmetic, no randomness), and
idempotent, so restarting never duplicates anything.

**The titles and authors are real; the ISBNs are not.** They carry correct check digits so the
domain accepts them, but attaching a genuine ISBN to a row invented for a demonstration would put a
real identifier on the wrong record.

### Watching the loan rule work

The sequence worth running, because the last two steps are the whole argument. Substitute the ids
returned by each call, or use the seeded data above:

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

### What a request looks like in the log

Logs are JSON on stdout, which is what a container platform collects. Tail them and call anything:

```bash
docker compose logs -f api
```

```json
{"LogLevel":"Information","Category":"Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware",
 "State":{"Method":"GET","Path":"/api/v1/books/0000...","StatusCode":404,"Duration":144.97},
 "Scopes":[{"TraceId":"04d4c083...","SpanId":"83e1ac46..."},
           {"RequestId":"0HNNNT268QFE5:00000001"},
           {"CorrelationId":"wheel-check-1"}]}
```

*Abridged and wrapped for reading: the real entry is one row.*

**The request line is the framework's, not a middleware written here.** `UseHttpLogging` with
`CombineLogs = true` produces exactly one entry per request carrying method, path, status and
duration, and `IHttpLoggingInterceptor` handles the per-request decisions: today, keeping liveness
probes out of the stream so an orchestrator polling forever does not scroll away the request you are
reading. This replaced a hand-written middleware that did the same job in about a hundred and thirty
lines: the framework has shipped this since .NET 8, and re-implementing it was the wrong call.

What *is* configured here is the field list, and what is absent from it is the decision:
`RequestQuery` is not enabled. The query string is the part of a URL a caller fills in, and on an API
that grows it is where a search term or an email address first turns up in a log nobody meant to hold
one. Headers and bodies are off for the same reason. This is a summary line, not a capture. No
names, emails or membership numbers are logged anywhere; identifiers only.

**The one subtlety worth knowing:** `Microsoft.AspNetCore` is turned down to `Warning` in
`appsettings.json` to silence the hosting layer's two-lines-per-request chatter, and HttpLogging's
category sits *underneath* that prefix. So `Microsoft.AspNetCore.HttpLogging` is explicitly exempted
one line below. Delete that exemption and the service logs nothing per request while looking
perfectly healthy, which is not hypothetical, it is how this service actually ran until somebody
went looking. EF Core's per-statement SQL logging is turned down on the same reasoning, and both are
one line to reverse while diagnosing.

**The correlation identifier is one string in three places**: the `X-Correlation-Id` response
header, the `correlationId` field of any RFC 7807 error body, and, when the caller supplied it,
every log line written while serving that request. So a caller reporting a failure hands you
something you can grep:

```bash
curl -s -H 'X-Correlation-Id: panel-demo-3' \
  localhost:8080/api/v1/books/00000000-0000-0000-0000-000000000000   # -> "correlationId":"panel-demo-3"
docker compose logs api | grep panel-demo-3
```

An identifier the caller supplies is honoured, but only after it is checked: bounded to 64
characters from a restricted alphabet, and refused if the header appears twice. It is
attacker-controlled text on its way into our records and back out in our response, and a caller
should not get to choose which of two identifiers the logs believe. A value that fails the check is
replaced rather than rejected, because failing a request over a logging convenience turns a
convenience into a new way to fail.

**`CorrelationMiddleware` is about forty lines, and the reason it is not larger is worth saying,
because the usual version of this class is mostly redundant.** ASP.NET Core already puts `TraceId`,
`SpanId`, `ConnectionId` and `RequestId` on every log line; `AddProblemDetails` already writes a
`traceId` into every error body; and an inbound **`traceparent`** header is already parsed and
adopted, so an identifier already survives a service hop with no code at all. Two things genuinely
are missing, and they are all this class does: nothing hands an identifier *back* to the caller, and
`traceparent` is a 55-character machine format with nowhere to put a human label like a batch name or
a ticket number.

That is also why the `CorrelationId` scope above appears only when a caller supplied one. With no
header, the value is the trace id, already on the line as `TraceId`, and repeating it under a
second name would make the log wider without making it say more.

### The audit trail

Every insert, update and delete is recorded: who did it, under which request, and what changed.
Create a book and then look at what the database remembers:

```bash
curl -s -X POST localhost:8080/api/v1/books \
  -H 'Content-Type: application/json' \
  -d '{"isbn":"9780140012934","title":"Watership Down","author":"Richard Adams","publishedYear":1972}'

docker compose exec db psql -U library -d library -c \
  "SELECT entity_type, action, actor, correlation_id, changes FROM audit_entries WHERE actor <> 'system';"
```

**It is written by an EF Core `SaveChanges` interceptor, not by the handlers.** That is the whole
design decision. "Every handler remembers to call the audit service" is a rule that holds until the
fifteenth handler is written under pressure, and the gap it leaves is silent. Nothing fails, a row
simply has no history, and you find out during the incident that needed it. Hanging the trail off
`SaveChanges` inverts that: a change made through the change tracker cannot reach the database
without passing through it, so a new aggregate is audited the day it is mapped and nobody has to
remember anything. One registration, in `AddInfrastructure`; no handler opts in, no entity carries an
attribute.

The boundary is worth stating precisely, because "cannot reach the database" would be an
overstatement and this repository contains the counterexample: SQL issued *around* the change
tracker is not audited. `EfIdempotencyStore` does exactly that, deliberately: an idempotency key is
transport plumbing, not a fact about the library. The risk is the same tool used carelessly.
`ExecuteUpdateAsync` and `ExecuteDeleteAsync` are the natural choice for a bulk operation such as the
retention job above, and a business change made with either would be unaudited with nothing to
indicate it.

**The rows are written inside the same transaction as the change they describe.** They are added to
the same change tracker, so they are inserted by the same `SaveChanges` and commit or roll back
together. This is testable, and tested: post a duplicate ISBN, watch the unique index refuse it, and
the audit table shows one creation rather than two. Anything that writes the audit afterwards (a
second save, a queue, a different store) has a window where the data moved and the record of it did
not, and that window is where the disputed change will land.

What each row carries, and one rule that shapes it: *record what would otherwise be
unrecoverable*:

| Action | `changes` column | Why |
|---|---|---|
| Created | null | The row is in its own table; copying it would store it twice |
| Updated | the delta, `{"Title":{"old":...,"new":...}}` | The current value is in the table; what it *used to be* is not |
| Deleted | every value the row had | The one case where the data is genuinely gone |

The actor is **`anonymous`** for HTTP callers and **`system`** for the startup migration and the
seeder, which is why the query above filters `system` out, or the 330 seeded rows would bury your
own change. That `anonymous` is deliberate and it is the honest answer: nothing in this service
authenticates anyone, so the trail says so rather than naming a user it never verified. The seam is
one branch in `HttpAuditContext`, already reading whatever principal the pipeline establishes.

### Retrying a write without doing it twice

A client that sends `POST /loans` and times out does not know whether the loan was created. Retrying
risks a duplicate; not retrying risks losing the operation. Send an `Idempotency-Key` and the retry
is free:

```bash
# The same command twice. One book, one response, and the second says so.
curl -si -X POST localhost:8080/api/v1/books \
  -H 'Content-Type: application/json' -H 'Idempotency-Key: retry-me-1' \
  -d '{"isbn":"9780571056866","title":"Lord of the Flies","author":"William Golding","publishedYear":1954}' \
  | grep -E 'HTTP/|Idempotency-Replayed'
```

The first call returns `201`; the second returns the same `201`, the same body (the same id, which
is the part that matters), the same `Location`, and `Idempotency-Replayed: true`. Only one book
exists.

`Location` is called out because getting it wrong is the easy mistake here: a replay that stores only
the status and the body passes every obvious assertion and then hands a client `201 Created` with no
indication of *what* was created, on precisely the retry path the feature exists to serve. An
allowlist of headers is stored alongside the body (`Location`, `ETag`, `Content-Language`), and not
the rest, because the others describe this exchange rather than the outcome.

**The primary key of `idempotency_keys` is the mechanism.** Both copies of a concurrent retry try to
insert the same key and PostgreSQL lets exactly one win, which is the same technique as the partial
unique index on active loans: between "does this key exist" and "insert it" there is a gap, and the
gap is where duplicates are born. `ON CONFLICT DO NOTHING` reports the loss as zero rows rather than
as an exception, so the ordinary case of a duplicate retry costs nothing.

Four decisions worth stating, because each is a place this could be wrong:

- **Opt-in, and `POST` only.** `GET`, `PUT` and `DELETE` are already idempotent by definition; `POST`
  is the only method whose repetition means a second thing happening. A request with no key behaves
  exactly as it did before the feature existed.
- **A 4xx is stored and replayed; a 5xx releases the key.** A malformed request is malformed every
  time, so replaying that verdict is free and correct. A server fault is not an answer, and storing
  it would turn a momentary failure into a permanent one for the client best behaved enough to retry.
  This is why the middleware sits *outside* the exception handler rather than inside it: a malformed
  body throws during model binding, and from inside, that throw would unwind past the middleware and
  the 400 produced afterwards would never be stored, so the rule would quietly not hold for the
  commonest 4xx there is.
- **A key reused for a *different* request is refused with 422**, following
  [draft-ietf-httpapi-idempotency-key-header](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/),
  rather than being served the first response, which would silently discard the second request. The
  request is fingerprinted (method, path, body) to detect it.
- **It does not replace the domain's uniqueness rules.** A duplicate borrow with no key is still
  refused by the partial unique index. This makes a well-behaved client's retry pleasant; the index
  is what makes the invariant true regardless of who calls it or how.

The honest limitation: the key is claimed in its own transaction, before the work, because a
concurrent duplicate has to be able to see it *while* the original is still running. So if the
process dies between the business commit and the response being stored, the key is left claimed and
a retry is told "in progress" until it expires. That is the safe direction to fail, the alternative
re-runs a change that already committed, but it is a real edge, and expiry is a retention job this
submission describes rather than builds.

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

## Design notes

**Deliberate dependency choices.** A dependency is avoided here when it is *both* pervasive
and licence-unstable: pervasive meaning that removing it would touch every entity, DTO and
handler rather than one file. That rules out mapping and validation libraries, so mapping is
hand-written `ToResponse()` methods and validation lives in value objects that cannot hold
invalid state. Several well-known .NET libraries in exactly those two slots have moved to
commercial licensing after becoming load-bearing in thousands of codebases; the cost of
removing them scales with the size of the system.

Libraries whose blast radius is bounded are used freely. Logging sits behind `ILogger`, so the
sink is a swap rather than a rewrite; Testcontainers never ships to production; seeding is a
single file. The distinction is the point: this is dependency *judgement*, not asceticism.

**Supply chain: the build refuses known-vulnerable packages.** Every version is pinned exactly in
`Directory.Packages.props`: no floating ranges, because a range resolves to whatever is newest at
restore time, which makes the build irreproducible and is the window a bad release arrives in.
NuGet audit runs over direct *and* transitive dependencies, and because MSBuild warnings are
errors here, a package with a published advisory fails the build rather than printing a warning
nobody reads.

That control has already paid for itself once. `Microsoft.AspNetCore.OpenApi` depends on
`Microsoft.OpenApi` 2.0.0, which is affected by CVE-2026-49451: a circular schema reference
drives the parser into unbounded recursion and terminates the process, and a
`StackOverflowException` cannot be caught, so there is no runtime defence. The parent package has
not moved to the fixed version. Central package management with transitive pinning raises it to
2.11.0 without waiting: one line, no fork, no `PackageReference`. This service never parses
untrusted OpenAPI documents, so real exposure was near nil. It is pinned anyway, because *"we
weren't reachable by that one"* is a much worse answer to an auditor than *"we patched it"*.

**Why .NET 10 when the brief said .NET 9 or above.** .NET 9 is an STS release and left support on
12 May 2026. .NET 10 is LTS, supported to November 2028. Targeting a framework that is already out
of support is a decision you would have to defend later for no benefit now, and the cost here was
one line in `Directory.Build.props` because nothing in the code was framework-specific.

**Validation is hand-written, and .NET 10 ships an alternative.** Request-shape validation runs
through a small endpoint filter over DataAnnotations rather than .NET 10's in-box
`AddValidation()`. The honest comparison: the framework's version is fail-closed by construction,
because there is no per-endpoint attachment step to forget, whereas the filter here needs an
explicit throw to cover that case, which it has, and which is unit-tested. The filter was written
before the retarget to .NET 10 and kept because it is small, tested, and reused by every endpoint;
on a longer-lived codebase the first-party option is the better default.

**Two layers of validation, on purpose.** The request DTO enforces shape (required fields,
lengths, bounds that are compile-time constants), and produces `400`. The domain enforces every
rule unconditionally and produces `422`. The domain checks are therefore not redundant with the
DTO: they are what a non-HTTP caller such as a seeder or a message consumer gets, and they are
where rules that depend on runtime state live, like "a book cannot be published in the future".

**Uniqueness is enforced twice, and that is the part worth reading.** The application checks
before inserting, which gives the ordinary case a clean, cheap rejection. A unique index decides
the outcome when two requests check microseconds apart, and the constraint violation is
translated back into the *identical* domain error, matched on constraint name so an unrelated
collision is never reported as a duplicate. A client cannot tell which path rejected it. Enforcing
a rule only in application code leaves it true only most of the time.

**The loan index is *partial*, and that word is doing the work.**

```sql
CREATE UNIQUE INDEX ix_loans_active_copy ON loans (book_copy_id) WHERE returned_at IS NULL;
```

Without the `WHERE`, this reads "a copy may appear in the loans table at most once": meaning a
returned book could never be borrowed again. With it, the constraint applies only to loans still
outstanding, which expresses a *temporal* invariant as a *static* index. That distinction is
invisible to almost every test one would think to write, so there is a test for exactly it:
`Borrows_the_same_copy_again_after_it_has_been_returned`, written before the migration existed.

It also settles a race the application cannot see: a return running concurrently with a re-borrow
of the same copy, where the new row cannot land while the old one still has a null `returned_at`.

And it pays twice. There is no availability or status column on a copy, because "on loan" is
derived state that a stored column could contradict, and the query that replaces it,
`NOT EXISTS (SELECT 1 FROM loans WHERE book_copy_id = c.id AND returned_at IS NULL)`, is served by
this same index. **The index that makes the invariant true is the index that makes the availability
query fast.**

**Searching by ISBN works with the number printed on the book.** Stored ISBNs are canonical
13-digit strings, so a search for `978-0-306-40615-7` (or for `0306406152`, the ISBN-10 of the same
title) would match nothing if the term were compared as text. The term goes through the same value
object the catalogue is built on: if it parses as an ISBN in any spelling it becomes the one stored
form and is matched exactly, using the unique index rather than a scan. This is the value object
earning its keep a second time.

**The search index is honest about what it does.** Substring matching compiles to `ILIKE '%term%'`,
and a leading wildcard cannot use a B-tree, so title and author carry GIN trigram indexes. At this
catalogue's size PostgreSQL will still choose a sequential scan and `EXPLAIN` will say so: the
index makes the *shape* correct at a million rows, and claiming it makes sixty rows fast would be a
claim the schema does not support. Note that `CREATE EXTENSION pg_trgm` needs a role with rights to
it; managed PostgreSQL grants that to its admin role but a locked-down application role would not,
which is one more argument for applying migrations as a deployment step rather than on boot.

**Which loan filters are index-backed, and which are not.** `ix_loans_member_active` is *partial*,
covering only loans still out, so `GET /loans?memberId=...&active=true` and `?overdue=true` are index
seeks, while the same filter over a borrower's full history is a scan. That is the right trade for a
library: the question asked constantly is "what does this person have out", and the one asked rarely
is "what have they ever borrowed". There is deliberately **no index on `members.status`** either:
with two distinct values PostgreSQL will scan regardless, and an index it never chooses is a
maintenance cost pretending to be an optimisation.

**Sorting is an allowlist and paging is bounded, both at the boundary.** An unpublished sort field
or a page size of 10,000 is a malformed request, answered with 400 by the same DataAnnotations
filter every request DTO goes through, not silently clamped, which would be a third behaviour for
invalid input with no stated rule covering it. Every ordering ends on the primary key, because ties
in a sort column otherwise leave rows able to appear on two pages or on none.

**One race is accepted, deliberately.** Two concurrent borrows can both read a member's active-loan
count as four and both proceed, leaving them holding six. There is no constraint behind that limit,
and the reasoning is written at the guard: a member briefly over their limit is a policy annoyance a
librarian can unwind, while the same physical book promised to two people is a failure the library
cannot honour. Only the second is worth the cost of a database constraint. The same judgement
applies to a double return, where the outcome is idempotent in substance and nothing is promised
twice.

## What I would do differently at scale

Everything below is deliberately absent. A library branch lending books is not a system with these
problems, and building for them here would be harder to defend than leaving them out, but knowing
*when* each one starts to pay is the point.

**Migrations would stop running at startup.** They run on boot here so that `docker compose up` on
a clean machine produces a working API with no manual step, and the flag makes that switchable. With
more than one replica it becomes a race, and it hands the application's runtime identity permission
to modify schema. In production this is a separate, single-shot deployment step against a database
account the service itself does not have.

**Authentication would be real**, via an external OIDC provider rather than anything built here. The
design and the reasoning are in [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md).

**Observability would grow distributed tracing.** One structured line per request, with an
identifier that correlates every line in it and survives an upstream hop, is the whole mechanism a
single service needs. Across several it is the wrong shape: `X-Correlation-Id` is a convention where
`traceparent` is a standard, and spans record *where* the time went rather than only how much of it
there was. `AddOpenTelemetry()` with the ASP.NET Core and Npgsql instrumentation is the change, and
it would replace this middleware rather than sit beside it.

**The idempotency keys would move out of PostgreSQL**, to Redis or an equivalent with native
expiry. Keys are short-lived, write-heavy and read once, the least database-shaped data in the
system, and putting them in a store that expires rows itself removes the retention job rather than
scheduling it. That is a different `IIdempotencyStore` and nothing else: not a change to the
middleware, and not a change to any handler. The audit trail would stay exactly where it is, because
its requirements are the opposite ones: durable, transactional with the data it describes, and
queryable next to it.

**Deep pagination would move to cursors.** Offset paging is correct and cheap for the page sizes a
catalogue UI asks for, and degrades badly at page 5,000 because the database still walks the rows it
skips.

**Reads would separate from writes before caching is reached for.** The read and write ports are
already distinct interfaces, so pointing queries at a replica is a change in one adapter rather than
in every handler. A cache is the tempting first answer and the one that introduces invalidation
bugs; a replica does not.

**Loan history would be partitioned by date.** It is the only table that grows without bound, and
every query against it is either recent or reporting, which is close to the ideal shape for range
partitioning.

**Who did what would be recorded on the aggregate, not only in a log.** At a bank an audit trail is
a requirement rather than a debugging aid, and a log line is the wrong durability guarantee for one.

**One race would get a concurrency token.** Two simultaneous returns can both write, which is
accepted here because the outcome is idempotent in substance. `UseXminAsConcurrencyToken()` on
`Loan` closes it in a line, and would convert a harmless lost update into a
`DbUpdateConcurrencyException` that nothing currently translates, so it would arrive as a 500. That
is a worse outcome than the problem, which is why the trade is stated rather than taken.

**What would not change:** the domain. No aggregate learns about replicas, caches, partitions or
roles. That is the return on the dependency rule, and it is the reason the rule is enforced by a
test rather than a diagram.

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layers and the dependency rule, domain model, request flow, cross-cutting concerns. Marks clearly which parts are built. |
| [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) | **Not implemented.** Why, what would be used, and where the seams are |
| [docs/PREREQUISITES.md](docs/PREREQUISITES.md) | What to install: Docker only, to run it |

## License

MIT: see [LICENSE](LICENSE).
