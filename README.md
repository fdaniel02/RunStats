# RunStats

[![CI](https://github.com/fdaniel02/RunStats/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/fdaniel02/RunStats/actions/workflows/ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=fdaniel02_RunStats&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=fdaniel02_RunStats)

Statistics from my runs, downloaded from Garmin Connect and shown in a web app. Runs in the k3s homelab cluster.

## Components

- `runstats`: Blazor Server app (.NET). Holds the UI and the sync, and owns the SQL Server database.
- `garmin-gateway` (#2): small Python service that talks to Garmin Connect through
  [python-garminconnect](https://github.com/cyberjunky/python-garminconnect). It has no database; it only passes
  Garmin's responses on to the app.

A Kubernetes CronJob triggers the sync; it can also be started from the UI.

The work is planned in the GitHub issues.

## Project layout

```
RunStats.slnx
src/RunStats/                the app (Blazor Server)
  Components/                app shell: App, routes, layout, error pages
  Features/<Screen>/         one folder per screen (Dashboard, Runs, Heatmap)
  Data/                      DbContext, migrations, startup and time zone helpers
tests/RunStats.Tests/        unit, component (bUnit) and SQL Server tests (xUnit v3)
.github/workflows/ci.yml     build and test on every pull request and push to main, publish the image from main
.github/dependabot.yml       weekly update pull requests for NuGet packages and GitHub Actions
```

NuGet versions are locked in `packages.lock.json` (enabled in `Directory.Build.props`); CI restores in locked mode.
After changing a package, commit the updated lock file too.

The lock file also holds a package the SDK adds implicitly, whose version follows the SDK. So when you change the
SDK version in `global.json`, regenerate the lock files in the same pull request:

```powershell
dotnet restore --force-evaluate
```

The Docker image builds with the newest .NET 10 SDK and runtime (`sdk:10.0` and `aspnet:10.0`) and restores without
locked mode, so it picks up .NET patches on every build without depending on `global.json`.

## Build and test

```powershell
dotnet build
dotnet run --project src/RunStats
dotnet test --coverlet
```

Tests run on Microsoft Testing Platform (set in `global.json`). `--coverlet` writes an OpenCover report
to `TestResults/coverage.opencover.*.xml`; the settings are in `tests/RunStats.Tests/testconfig.json`.

Some tests run against a real SQL Server in a container (Testcontainers), so Docker must be running.
The first run downloads the SQL Server image (about 1.5 GB).

### Database migrations

`dotnet-ef` is a local tool (`dotnet tool restore`). It needs a connection string to build the app, but doesn't
connect for adding a migration:

```powershell
$env:ConnectionStrings__Default = "Server=unused;Database=unused"
dotnet ef migrations add <Name> --project src/RunStats
```

A test fails when the model has changes without a migration.

## Configuration

The app needs the connection string `ConnectionStrings:Default`. It is not stored in `appsettings.json`;
the app refuses to start without it.

### Time zone

"This month", "this year" and weekly totals use the time zone in the `TimeZone` setting (an IANA id,
default `Europe/Amsterdam` in `appsettings.json`), not the server's. Override it in Docker with `-e TimeZone=...`.

### Logging

Logs go to stdout as one JSON object per line, for a log collector (e.g. Grafana Alloy → Loki) to pick up.
Locally (`Development`) they use the plain console format. Each log message has a fixed event id (see `src/RunStats/Log.cs`).

Traces and metrics are sent over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

At startup the app applies database migrations. While SQL Server is unreachable it tries 15 times, 4 seconds apart, and logs
each failed attempt as a warning. If the host doesn't answer at all, each attempt also waits for the connect timeout (15 s by default).
Errors that waiting won't fix (wrong password, missing database, locked or expired login) stop it at once.
A startup failure is logged as `Critical` and the process exits with code 1.

### Local development

The connection string lives in user-secrets (stored in `%APPDATA%\Microsoft\UserSecrets\`, outside the project):

```powershell
dotnet user-secrets set --project src/RunStats "ConnectionStrings:Default" "Server=<sql-host>;Database=RunStats;User Id=<login>;Password=<password>;TrustServerCertificate=True"
```

User-secrets are only loaded when `ASPNETCORE_ENVIRONMENT=Development` (the default in `launchSettings.json`).

### Docker

Every push to `main` publishes the image to GitHub Container Registry, tagged `sha-<short commit sha>` and `latest`,
after the tests pass and a Trivy scan finds no fixable critical vulnerabilities:

```powershell
docker pull ghcr.io/fdaniel02/runstats:latest
```

Or build the image yourself from the repository root:

```powershell
docker build -t runstats .
```

The container runs as the non-root `app` user.

Pass the connection string as an environment variable (`__` stands for `:`), preferably from a file so the password
stays out of your shell history:

```powershell
docker run -d -p 8080:8080 --env-file runstats.env runstats
```

`runstats.env` contains a single line, without quotes:

```
ConnectionStrings__Default=Server=<sql-host>;Database=RunStats;User Id=<login>;Password=<password>;TrustServerCertificate=True
```

Keep that file outside the project folder, so it can't end up in the image or in git.
