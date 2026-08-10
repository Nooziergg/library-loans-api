# Architecture — Library Loans API

## 1. Deployment view

Everything a reviewer needs is two containers behind one command.

```mermaid
flowchart LR
    R["Reviewer<br/>requests.http · curl"]
    subgraph compose["docker compose up"]
        direction LR
        API["<b>api</b><br/>ASP.NET Core 10<br/>:8080"]
        DB[("<b>db</b><br/>PostgreSQL 16<br/>volume: pgdata")]
    end
    LOGS["stdout — JSON lines<br/>docker compose logs -f api"]

    R -->|"Bearer JWT"| API
    API -->|"EF Core / Npgsql"| DB
    API --> LOGS
```

The `api` service waits on the `db` healthcheck, applies migrations, then seeds if the
database is empty. No manual DB creation step — see `AUTH.md`.

## 2. Layers and the dependency rule

Arrows are compile-time references. **They only point inward.**

```mermaid
flowchart TD
    API["<b>LibraryLoans.Api</b><br/>minimal APIs · middleware · DI composition root"]
    INF["<b>LibraryLoans.Infrastructure</b><br/>EF Core · migrations · seed · adapters"]
    APP["<b>LibraryLoans.Application</b><br/>use cases · ports it owns · Result&lt;T&gt;"]
    DOM["<b>LibraryLoans.Domain</b><br/>aggregates · value objects · invariants<br/><i>zero package references</i>"]

    API --> APP
    API -.->|"DI wiring in Program.cs only"| INF
    INF --> APP
    INF --> DOM
    APP --> DOM

    style DOM fill:#1f6f43,stroke:#0d3b24,color:#fff
    style APP fill:#245a8d,stroke:#12304a,color:#fff
```

The dashed arrow is the only concession: `Api` references `Infrastructure` purely to
register implementations in `Program.cs`. Nothing else in `Api` may use an
`Infrastructure` type.

`Application` **owns** the port interfaces (`IBookRepository`, `ILoanRepository`,
`IUnitOfWork`); `Infrastructure` implements them. That is the dependency inversion that
makes `Application` testable with hand-written fakes and no database.

**Enforced by test, not by convention:** `ArchitectureTests.Domain_has_no_infrastructure_dependencies`
reflects over the Domain assembly and fails the build if EF Core or ASP.NET Core ever
appears in its reference graph.

## 3. Domain model

```mermaid
erDiagram
    BOOK ||--o{ BOOK_COPY : "has"
    BOOK_COPY ||--o{ LOAN : "loaned in"
    MEMBER ||--o{ LOAN : "borrows"
    USER_ACCOUNT }o--|| MEMBER : "may authenticate as"

    BOOK {
        uuid Id PK
        string Isbn UK "value object, checksum-validated"
        string Title
        string Author
        int PublishedYear
    }
    BOOK_COPY {
        uuid Id PK
        uuid BookId FK
        string Barcode UK
        string Status "Available | OnLoan | Retired"
    }
    MEMBER {
        uuid Id PK
        string MembershipNumber UK
        string Name
        string Email
        string Status "Active | Suspended"
    }
    LOAN {
        uuid Id PK
        uuid BookCopyId FK "partial unique WHERE ReturnedAt IS NULL"
        uuid MemberId FK
        timestamptz LoanedAt
        timestamptz DueAt
        timestamptz ReturnedAt "null = active"
        int RenewalCount
    }
    USER_ACCOUNT {
        uuid Id PK
        string Username UK
        bytes PasswordHash "PBKDF2"
        bytes PasswordSalt
        string Role "librarian | member"
        uuid MemberId FK "null for librarians"
    }
```

## 4. State machines

### Loan

```mermaid
stateDiagram-v2
    [*] --> Active: Loan.Open()
    Active --> Active: Renew() — max 2, and only if not overdue
    Active --> Returned: Return()
    Returned --> [*]

    note right of Active
        Overdue is DERIVED, never stored:
        ReturnedAt is null AND DueAt &lt; TimeProvider.GetUtcNow()

        No background job flips a status column.
        A stored "Overdue" state would be a lie
        between the due instant and the job run.
    end note
```

Guards on `Loan.Open()` — all enforced inside the aggregate, none in a service:
- the copy has no active loan (**also** a DB partial unique index)
- the copy is not `Retired`
- the member is `Active`, not `Suspended`
- the member holds fewer than 5 active loans

`Return()` on an already-returned loan is a **domain error**, not a silent no-op.

### BookCopy

```mermaid
stateDiagram-v2
    [*] --> Available: AddCopy()
    Available --> OnLoan: loan opened
    OnLoan --> Available: loan returned
    Available --> Retired: Retire()
    Retired --> [*]

    note left of OnLoan
        Retire() from OnLoan is rejected.
        A copy in a borrower's hands
        cannot be removed from the catalogue.
    end note
```

### Member

```mermaid
stateDiagram-v2
    [*] --> Active: Register()
    Active --> Suspended: Suspend()
    Suspended --> Active: Reinstate()

    note right of Suspended
        Suspension is allowed while holding
        active loans — it blocks NEW borrows,
        it does not recall existing ones.
    end note
```

## 5. Request flow — the borrow path

This is the sequence worth reading, because it shows the invariant enforced at two levels
and what happens when a race is lost.

```mermaid
sequenceDiagram
    actor C as Client
    participant EP as POST /api/v1/loans
    participant H as BorrowCopyHandler<br/>(Application)
    participant AG as Loan<br/>(Domain)
    participant RP as ILoanRepository<br/>(Infrastructure)
    participant PG as PostgreSQL

    C->>EP: Bearer JWT + { memberId, bookCopyId }
    Note over EP: 401 here if unauthenticated —<br/>default-deny fallback policy
    EP->>H: BorrowCopyCommand + CancellationToken
    H->>RP: load copy, member, active-loan count
    RP->>PG: SELECT ... AsNoTracking()
    H->>AG: Loan.Open(copy, member, now, policy)
    alt an invariant fails
        AG-->>H: Result.Failure(DomainError)
        H-->>EP: e.g. MemberSuspended
        EP-->>C: 422 ProblemDetails
    else invariants hold
        AG-->>H: Result.Success(loan)
        H->>RP: Add(loan) + SaveChangesAsync(ct)
        RP->>PG: INSERT
        alt concurrent borrow won the race
            PG-->>RP: 23505 unique_violation
            RP-->>H: ConflictError
            EP-->>C: 409 ProblemDetails
        else
            PG-->>RP: OK
            EP-->>C: 201 Created + Location
        end
    end
```

**Why both checks.** The aggregate check gives a clean, fast, well-worded rejection for the
common case. The partial unique index is the one that cannot be raced — two requests can
both pass the in-memory check microseconds apart, and exactly one INSERT survives. Catching
`23505` and translating it to a 409 is what makes the rule actually true under concurrency.
Enforcing it only in the aggregate is the mistake most submissions make.

## 6. Cross-cutting concerns

| Concern | Mechanism | Package |
|---|---|---|
| Structured logging | Serilog → JSON console sink, message templates, no string interpolation | Serilog |
| Request logging | `UseSerilogRequestLogging()` — one enriched line per request with method, path, status and elapsed ms | Serilog |
| Correlation | middleware generates/echoes `X-Correlation-Id`, pushed as a log scope so every line in a request carries it | built-in |
| Error shaping | `IExceptionHandler` + `AddProblemDetails()` → RFC 7807. Exception messages never reach the client. | built-in |
| Validation | value objects reject invalid state at construction; `Result<T>` carries failures outward; DataAnnotations for request-shape only | built-in |
| AuthN/AuthZ | JWT bearer, `FallbackPolicy = RequireAuthenticatedUser`, role policies | first-party |
| Time | `TimeProvider` injected everywhere — never `DateTime.UtcNow` | built-in |
| Health | `/health/live` (process) and `/health/ready` (DB probe) | built-in |
| Rate limiting | fixed window on write endpoints | built-in |
| Resilience | `EnableRetryOnFailure` on the Npgsql connection | Npgsql |

`TimeProvider` deserves the callout: it makes "is this loan overdue" a deterministic unit
test instead of a `Thread.Sleep`, and it is first-party since .NET 8.

### What never gets logged

Logs are structured, which means every parameter in a message template becomes a queryable
field that outlives the request and gets shipped wherever logs go. So the rule is stated before
there is anything to break it: **personal data does not go into a log line.** Identifiers do —
`MemberId`, `LoanId`, `BookId` — because they are meaningless outside the database and are
exactly what an investigation needs. Names, email addresses and membership numbers do not.

Written down now, ahead of `Member` arriving in P2, because the alternative is discovering an
email address in a log field during a review and retrofitting the rule across every handler.

## 7. Reading order for a reviewer

The README will point here and say: read `Domain/Loans/Loan.cs` first — one file that
contains every rule the system enforces. Then `Application/Loans/BorrowCopyHandler.cs`,
then the migration containing the partial unique index, then
`IntegrationTests/Loans/ConcurrentBorrowTests.cs`. Four files tell the whole story.
