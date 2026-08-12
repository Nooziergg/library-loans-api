# Walkthrough

Everything below runs against `docker compose up` on a clean machine. The point of the page is that
the rules are *shown* rather than described: every number and status code here was produced by
running the command against a container built from an empty volume.

Start the stack first, if it is not already up:

```bash
docker compose up
```

For the setup itself and the first few calls, see the [quick start](../README.md#quick-start).

## Three things worth trying, because they show the rules rather than describing them

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

## How the seed is built, and why it matters

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

## Watching the loan rule work

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

## What a request looks like in the log

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

## The audit trail

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

## Retrying a write without doing it twice

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
