# RunStats

[![CI](https://github.com/fdaniel02/RunStats/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/fdaniel02/RunStats/actions/workflows/ci.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=fdaniel02_RunStats&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=fdaniel02_RunStats)

Statistics from my runs, downloaded from Garmin Connect and shown in a web app. Runs in the k3s homelab cluster.

## Components

- `runstats`: Blazor Server app (.NET). Holds the UI and the sync, and owns the SQL Server database.
- `garmin-gateway`: small Python service that talks to Garmin Connect through
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
  Data/                      DbContext, entities, migrations, startup and time zone helpers
  Garmin/                    client for the Garmin gateway
  Sync/                      the sync with Garmin and its API
tests/RunStats.Tests/        unit, component (bUnit) and SQL Server tests (xUnit v3)
gateway/                     the Garmin gateway (Python, FastAPI), see below
.github/workflows/ci.yml     build and test on every pull request and push to main, publish the image from main
.github/dependabot.yml       weekly update pull requests for NuGet, uv (gateway) and GitHub Actions
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

### Garmin gateway address

`GarminGateway:BaseUrl` is where the app reaches the gateway: `http://garmin-gateway:8080/` (the Service name in the
cluster) in `appsettings.json`, `http://localhost:8081/` in `appsettings.Development.json`.

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

## Sync

A sync reads Garmin's whole activity list through the gateway and keeps the runs: type `running` (id 1) and its sub types
(parent type id 1: treadmill, trail, track, obstacle runs, ...). It compares each run's list entry with the stored one:

- new run: downloads its detail, laps, heart rate zones, weather and FIT file and stores them as they came
  (`RunRawData`), together with a `Runs` row with the basics from the list
- changed list entry (e.g. renamed, marked as a race): downloads and stores the run again
- unchanged: nothing is downloaded
- gone from Garmin: deleted, only when the whole list was read

Each run is stored in its own transaction, so a sync that stops halfway (rate limit, failed login, app stopping) keeps
the runs it stored, and the next sync continues from there. Downloading a run takes about 6 s, because the gateway
spaces its calls to Garmin.

Only one sync runs at a time. Each is recorded in `SyncRuns` with its progress and result; a sync that was running
when the app stopped is marked as interrupted at the next start.

```powershell
curl.exe -X POST "http://localhost:5310/api/sync?trigger=manual"   # 202, Location: /api/sync/{id}
curl.exe http://localhost:5310/api/sync/1                          # status, counts and error
```

`trigger` is `schedule` (default) or `manual`. A request while a sync runs returns that sync (`"started": false`).

Telemetry: a `Sync` span (the gateway's spans join its trace), log events 10–16, and the metrics `runstats.syncs`
(by trigger and outcome), `runstats.sync.duration`, `runstats.sync.runs` (by change: added, updated, deleted) and
`runstats.sync.last_success` (Unix time of the last successful sync).

## Garmin gateway

`gateway/` is the only part that talks to Garmin Connect. It returns Garmin's responses unchanged; the app decides what
to keep. It needs [uv](https://docs.astral.sh/uv/) (`winget install --id astral-sh.uv -e`), which also installs the
Python version from `gateway/.python-version` if it's missing. Dependencies are locked in `gateway/uv.lock`.

```powershell
cd gateway
uv sync
uv run ruff check
uv run ruff format --check
uv run pytest --cov
```

The tests use a fake Garmin client; they never call Garmin.

### Endpoints

| Endpoint | Returns |
| --- | --- |
| `GET /activities?start=0&limit=100` | A page of activities, newest first (`limit` up to 1000) |
| `GET /activities/{id}` | The full summary of one activity |
| `GET /activities/{id}/splits` | Laps |
| `GET /activities/{id}/hr-zones` | Time in heart rate zones |
| `GET /activities/{id}/weather` | Weather during the activity |
| `GET /activities/{id}/fit` | The original FIT file (404 for activities imported without one) |
| `GET /personal-records` | Personal records |
| `POST /fit/samples` | Decodes the FIT file in the request body into one sample per second (see below) |
| `GET /healthz/live`, `GET /healthz/ready` | Up / logged in to Garmin |

`/fit/samples` doesn't call Garmin: the app sends a FIT file it has stored, so runs can be processed again without
downloading them. It uses [fitdecode](https://github.com/polyvertex/fitdecode) (MIT), not Garmin's FIT SDK, whose
license doesn't allow distributing it in a public image. Each sample has its offset in seconds from the first record,
position in semicircles, distance, speed, altitude, heart rate, cadence in steps per minute (FIT counts strides, with
the fraction in a separate field), step length, ground contact time, vertical oscillation and ratio, power and
temperature; missing values are `null`. When a second has more than one record, the last one is used. Files over 20 MB
get `413`, files that aren't valid FIT get `422`.

Errors: `404` when Garmin doesn't know the activity, `502` when a Garmin call fails, and `503` with `Retry-After`
while the gateway waits after a rate limit or a failed login.

### Login and limits

At startup the gateway logs in: with the saved token in `GARMIN_TOKEN_DIR` if there is one, otherwise with the email
and password (the account has no two-factor authentication). Garmin refreshes the token itself; when that fails, the
gateway logs in again once. The token file holds the Garmin session, so keep it as private as the password.

Calls to Garmin run one at a time, at least `GARMIN_MIN_SECONDS_BETWEEN_CALLS` apart. After a rate limit (HTTP 429)
the gateway doesn't call Garmin for `GARMIN_RATE_LIMIT_COOLDOWN_SECONDS`; after a failed login it doesn't try again
for `GARMIN_LOGIN_FAILURE_COOLDOWN_SECONDS`, because repeated failed logins can get the account locked.

| Setting | Default |
| --- | --- |
| `GARMIN_EMAIL`, `GARMIN_PASSWORD` | none |
| `GARMIN_TOKEN_DIR` | `/data` |
| `GARMIN_MIN_SECONDS_BETWEEN_CALLS` | `1` |
| `GARMIN_RATE_LIMIT_COOLDOWN_SECONDS` | `300` |
| `GARMIN_LOGIN_FAILURE_COOLDOWN_SECONDS` | `900` |
| `HOST` | `127.0.0.1` (`0.0.0.0` in the Docker image) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | none: no traces or metrics |
| `OTEL_SERVICE_NAME` | `garmin-gateway` |
| `PORT` | `8080` |

Logs are JSON on stdout with the same field names as the app's; health check requests aren't logged.
Log lines written during a request carry its `TraceId` and `SpanId`.

### Telemetry

With `OTEL_EXPORTER_OTLP_ENDPOINT` set (e.g. `http://alloy:4318`), traces and metrics go to that collector over
OTLP/HTTP. Requests are traced (health checks aren't); a request from the app continues the app's trace. Each call to
Garmin is a span `Garmin <operation>` with its outcome and the time it waited for the throttle. Spans and metrics hold
operation names, outcomes and timings only: no credentials, tokens, Garmin data or error messages.

| Metric | Attributes |
| --- | --- |
| `garmin.calls` | `garmin.operation`, `garmin.outcome` (`ok`, `not_found`, `unauthorized`, `rate_limited`, `error`) |
| `garmin.call.duration` (s, without the throttle wait) | same |
| `garmin.rate_limits` | `garmin.operation` |
| `garmin.logins` | `garmin.login.method` (`saved_token`, `password`), `garmin.outcome` (`ok`, `failed`, `rate_limited`, `error`) |

### Run locally

Put the credentials in a file outside the repository, e.g. `%USERPROFILE%\.runstats\gateway.env`:

```
GARMIN_EMAIL=...
GARMIN_PASSWORD=...
```

```powershell
docker build -t runstats-garmin-gateway gateway
docker run -d --name garmin-gateway -p 8081:8080 --read-only `
  --env-file $env:USERPROFILE\.runstats\gateway.env -v garmin-gateway-data:/data runstats-garmin-gateway
```

The image is published as `ghcr.io/fdaniel02/runstats-garmin-gateway` together with the app's. It runs as user
`10001` and only writes to `/data`.
