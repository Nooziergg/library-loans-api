# Library Loans API

A CRUD API for a lending library, built to demonstrate Clean Architecture, enforced domain
invariants, and the cross-cutting concerns a production service needs.

## Quick start

The only thing you need installed is **Docker** (or Podman).

```bash
git clone <repo-url> && cd library-loans
docker compose up
```

Then:

```bash
curl http://localhost:8080/health/live      # -> Healthy
```

PowerShell:

```powershell
Invoke-RestMethod http://localhost:8080/health/live
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

**Structured logging.** JSON lines on stdout, which is what a container platform collects.
No file sinks, no log-shipping agent inside the container.

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layer diagram, domain model, state machines, request flow |
| [docs/AUTH.md](docs/AUTH.md) | How the database is created and how to authenticate |
| [docs/PREREQUISITES.md](docs/PREREQUISITES.md) | What to install |

## License

MIT — see [LICENSE](LICENSE).
