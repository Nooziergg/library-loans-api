# Prerequisites — install these manually

Two tiers. **Tier 1 is what a reviewer needs** (and is the only thing the README will
require). Tier 2 is what *we* need to develop it.

## Tier 1 — required to run the submission

| # | Tool | Version | Notes |
|---|---|---|---|
| 1 | **Docker Desktop** *or* **Podman + podman-compose** | Docker 24+ / Podman 5+ | The whole system comes up with one command. Podman works — the compose file avoids Docker-only syntax. On Podman: `podman machine start` first. |
| 2 | **Git** | 2.40+ | Already present. |

That is the entire reviewer-facing requirement. No .NET SDK, no Postgres install, no
Azure account. This is deliberate and gets stated in the README.

## Tier 2 — required to develop

| # | Tool | Version | Install |
|---|---|---|---|
| 3 | **.NET SDK 10** | 10.0.302 ✅ installed | Pinned by `global.json`. .NET 9 is STS and left support on 12 May 2026; .NET 10 is LTS to Nov 2028. |
| 4 | **dotnet-ef CLI** | 10.x | `dotnet tool install --global dotnet-ef --version 10.*` — needed to scaffold migrations. Must match the EF Core package version, or every scaffold prints a version-mismatch warning. |
| 5 | **REST client** | any | VS Code **REST Client** extension, or Rider/Visual Studio built-in `.http` support. We ship a `requests.http`. |

Note: `dotnet test` needs Docker **running** — the integration suite uses Testcontainers to
start a disposable PostgreSQL per run. The unit suite needs nothing.

### Running the integration suite under Podman

Testcontainers speaks the Docker API, so Podman works — but not out of the box on Windows.
Two environment variables are usually required, and the second one is the one that is easy to
lose an hour to:

```powershell
podman machine start
$env:DOCKER_HOST = "npipe:////./pipe/podman-machine-default"
```

`DOCKER_HOST` points Testcontainers at Podman's socket instead of a Docker daemon that is not
there.

You do **not** need `TESTCONTAINERS_RYUK_DISABLED` — the suite already sets it, in
`tests/LibraryLoans.IntegrationTests/Infrastructure/TestRunConfiguration.cs`. Ryuk is the
sidecar Testcontainers normally starts to reap leftover containers, and it works by mounting the
Docker socket, which hands a container control of the daemon for the whole run. The fixture owns
one container and disposes it itself, so that privilege buys nothing here. It is disabled in
code rather than left to each developer's environment, which also happens to remove the most
common first failure under rootless Podman, where Ryuk cannot start and the error looks like an
unrelated timeout.

Without a container runtime, `dotnet test tests/LibraryLoans.UnitTests` still runs the entire
unit suite, which covers every domain rule.

## Tier 3 — optional, quality of life

| # | Tool | Why |
|---|---|---|
| 6 | **GitHub CLI (`gh`)** | `gh repo create` + pushing without leaving the terminal. |
| 7 | **DBeaver** or **pgAdmin** | Eyeball the seeded data and confirm the partial unique index exists. `psql` inside the container also works: `docker compose exec db psql -U library -d library`. |
| 8 | **k6** or `hey` | Only if we do the optional load-test appendix in the README. |

## What we deliberately do NOT install

- **SQL Server** — Postgres was chosen partly because SQL Server on Apple Silicon is a
  liability, and the brief requires Mac *or* Windows.
- **Redis** — no distributed cache in this build. It is listed in the README's
  "what I'd do differently at scale" section as a deliberate omission.
- **Any mocking / mapping / validation library** — mapping is hand-written and validation lives
  in value objects. See the dependency rationale in the README.

## Verify your environment

```bash
docker --version          # or: podman --version
docker compose version    # or: podman-compose --version
dotnet --list-sdks        # expect 9.0.3xx
dotnet-ef --version       # expect 9.x
git --version
```
