# InventoryApp

[![.NET CI](https://github.com/MacMbizo/InventoryApp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/MacMbizo/InventoryApp/actions/workflows/dotnet.yml)

Production-grade Windows desktop application (WPF, .NET) with CI/CD, diagnostics, and observability baked-in.

## CI/CD Overview

The repository includes a complete .NET CI workflow that runs on Windows runners and delivers:

- Automated restore, build, and tests (unit/integration)
- Headless desktop startup and DB migration smoke
- UI smoke tests (xUnit + FlaUI) with continue-on-error to reduce flakiness on CI
- Diagnostics export bundle
- Runtime logs and crash dumps collection and upload as artifacts
- Portable publish (zip) artifact for Desktop app
- Sentry integration via environment variable `SENTRY_DSN`

Workflow: .github/workflows/dotnet.yml

### Jobs
- build-test (default on push/PR)
  - Build and test solution
  - Run smoke tools and desktop headless verification
  - Publish portable desktop zip
  - Export diagnostics bundle
  - Upload logs, dumps, and test results artifacts
- crash-test (manual, workflow_dispatch)
  - Publishes Desktop
  - Triggers two synthetic crash paths in the app:
    - `--crash-test=sentry` (captures exception to Sentry)
    - `--crash-test=dump` (Environment.FailFast to produce a minidump)
  - Collects logs and dumps as artifacts

## Sentry DSN Setup
To enable Sentry ingestion in CI:
1. In GitHub → Repository Settings → Secrets and variables → Actions → New repository secret
2. Name: `SENTRY_DSN`
3. Value: Sentry Project DSN (do not commit secrets)

The workflow automatically exposes `SENTRY_DSN` to both jobs. If not set, Sentry is skipped and only logs/dumps are produced.

## Manual Crash Test Run
To validate end-to-end crash handling:
1. Go to Actions → ".NET CI" → "Run workflow"
2. Select branch → Run
3. Wait for `crash-test` job to run and finish
4. Verify in Sentry that an event was captured (synthetic crash)
5. Download artifacts:
   - crash-test-logs
   - crash-test-dumps (minidumps)

## Artifacts
- KitchenInventory.Desktop-win-x64.zip: Published portable app
- diagnostics-bundle: Exported diagnostics zip
- InventoryApp-logs / crash-test-logs: Serilog logs collected from the runner
- crash-dumps / crash-test-dumps: Any *.dmp/*.mdmp found on the runner
- test-results: TRX reports

## Local Development
- Restore and build: `dotnet build InventoryApp.sln`
- Run smoke tool: `dotnet run --project ./src/KitchenInventory.Smoke`
- Run desktop (UI): run `KitchenInventory.Desktop` project or its EXE in `bin/Debug|Release/net8.0-windows`
- Headless check: set `INVENTORY_HEADLESS=1` and invoke the desktop with `--headless`

## Project Structure
- src/KitchenInventory.Desktop: WPF application
- src/KitchenInventory.Data: EF Core data access + migrations
- src/KitchenInventory.Smoke: CLI smoke for DB and basic checks
- tests/KitchenInventory.Desktop.Tests: xUnit tests (unit/integration/UI-smoke)
- .github/workflows/dotnet.yml: CI pipeline