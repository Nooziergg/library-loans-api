# syntax=docker/dockerfile:1

# ---- build -------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against project files only, so editing source does not invalidate the
# (slow) restore layer.
# Directory.Packages.props is not optional here. MSBuild finds it by walking up from the
# project directory, and inside this image that walk ends at /src, so if it is missing,
# central package management is silently off, every PackageReference loses its version, and
# restore fails. It is easy to leave out precisely because a build at the repo root always
# finds it.
COPY global.json NuGet.config Directory.Build.props Directory.Packages.props ./
COPY src/LibraryLoans.Domain/LibraryLoans.Domain.csproj                 src/LibraryLoans.Domain/
COPY src/LibraryLoans.Application/LibraryLoans.Application.csproj       src/LibraryLoans.Application/
COPY src/LibraryLoans.Infrastructure/LibraryLoans.Infrastructure.csproj src/LibraryLoans.Infrastructure/
COPY src/LibraryLoans.Api/LibraryLoans.Api.csproj                       src/LibraryLoans.Api/
RUN dotnet restore src/LibraryLoans.Api/LibraryLoans.Api.csproj

COPY src/ src/
RUN dotnet publish src/LibraryLoans.Api/LibraryLoans.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# curl exists solely so HEALTHCHECK below can run. Installed as root, before privileges
# are dropped; the layer is cleaned up in the same step so it leaves no package index.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

# APP_UID is defined by the base image (non-root). Never run the API as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "LibraryLoans.Api.dll"]
