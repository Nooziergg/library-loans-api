# Prerequisites

## To run it: Docker, and nothing else

| Tool | Version |
|---|---|
| **Docker Desktop**, or **Podman** | Docker 24+ / Podman 5+ |

That is the whole requirement. No .NET SDK, no PostgreSQL installation, no cloud account, no
configuration to edit. `docker compose up` builds the API, starts PostgreSQL, waits for it to be
healthy, applies migrations and serves on port 8080.

Podman works — the compose file avoids Docker-only syntax. Run `podman machine start` first.

The database is published on host port **55432**, deliberately not the default 5432, so it cannot
collide with a PostgreSQL you already have running. Connect a tool to it there, or use psql inside
the container:

```bash
docker compose exec db psql -U library -d library
```

## To run the tests

The unit suite needs nothing installed beyond the .NET SDK:

```bash
dotnet test tests/LibraryLoans.UnitTests
```

The integration suite additionally needs a **running container runtime**. It uses Testcontainers to
start a disposable PostgreSQL on a random port, migrate it, and destroy it when the run ends — which
is how the suite satisfies the requirement that tests never touch a development database. There is
nothing to set up and nothing to clean up:

```bash
dotnet test
```

### Under Podman

Testcontainers speaks the Docker API, so Podman works, but not out of the box on Windows:

```powershell
podman machine start
$env:DOCKER_HOST = "npipe:////./pipe/podman-machine-default"
```

That points Testcontainers at Podman's socket rather than a Docker daemon that is not there.

You do **not** need `TESTCONTAINERS_RYUK_DISABLED`. The suite sets it in
`tests/LibraryLoans.IntegrationTests/Infrastructure/TestRunConfiguration.cs`, with the reasoning in
that file: Ryuk is the sidecar Testcontainers normally starts to reap leftovers, and it works by
mounting the Docker socket — handing a container control of the daemon for the whole run, to cover
a case that only arises when a run crashes. The fixture owns one container and disposes it itself.
Disabling it in code also removes the most common first failure under rootless Podman, where Ryuk
cannot start and the error looks like an unrelated timeout.

## To develop it

| Tool | Version | Notes |
|---|---|---|
| **.NET SDK 10** | 10.0.302 | Pinned by `global.json`. .NET 9 is STS and left support on 12 May 2026; .NET 10 is LTS to November 2028. |
| **dotnet-ef** | 10.x | `dotnet tool install --global dotnet-ef --version 10.*`. Only needed to scaffold new migrations; the committed ones apply without it. Keep it on the same version as the EF Core packages or every scaffold prints a mismatch warning. |

## Verify your environment

```bash
docker --version          # or: podman --version
docker compose version
dotnet --list-sdks        # expect 10.0.3xx
dotnet-ef --version       # expect 10.x
```
