# Design notes and what would change at scale

Why this system is put together the way it is, which dependencies were refused and on what test, and
the specific points at which each decision would stop being the right one.

The structural story is in [ARCHITECTURE.md](ARCHITECTURE.md); this page is the reasoning behind the
choices rather than the shape they produced.

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
design and the reasoning are in [docs/AUTHORIZATION.md](AUTHORIZATION.md).

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
