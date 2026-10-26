# RunStats

Statistics from my runs, downloaded from Garmin Connect and shown in a web app. Runs in the k3s homelab cluster.

## Components

- `runstats`: Blazor Server app (.NET). Holds the UI and the sync, and owns the SQL Server database.
- `garmin-gateway`: small Python service that talks to Garmin Connect through
  [python-garminconnect](https://github.com/cyberjunky/python-garminconnect). It has no database; it only passes
  Garmin's responses on to the app.

A Kubernetes CronJob triggers the sync; it can also be started from the UI.

The work is planned in the GitHub issues.
