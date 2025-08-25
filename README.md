# InventoryApp

[![.NET CI](https://github.com/MacMbizo/InventoryApp/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/MacMbizo/InventoryApp/actions/workflows/dotnet.yml)

Production-grade Windows desktop app (WPF, .NET 8) for managing kitchen inventory with reliable CI, diagnostics, and crash observability.

What you get
- Automated CI on push/PR (Windows runner)
- Restore, build (Release), smoke checks (DB migrate + query), headless desktop startup
- Tests: non-UI (all) + UI smoke (non-blocking)
- Portable publish (win-x64) zipped and uploaded as an artifact
- Diagnostics bundle export and runtime logs/dumps collection
- Sentry crash/error reporting (DSN via secret)

Workflows
- build-test (push/PR): full build + tests + artifacts
- crash-test (manual): workflow_dispatch; runs synthetic Sentry crash and hard dump, then uploads logs/dumps

Setup (one time)
1) Add repository secret SENTRY_DSN in GitHub Settings → Secrets and variables → Actions → New repository secret.
2) Push changes to main to trigger build-test automatically.
3) To run crash-test: Actions → .NET CI → Run workflow → select main → Run.

Local development
- Restore: dotnet restore ./InventoryApp.sln
- Build: dotnet build ./InventoryApp.sln -c Release --no-restore
- Tests (non-UI): dotnet test ./InventoryApp.sln -c Release --no-build --filter "Category!=UI"
- Smoke: dotnet run --project ./src/KitchenInventory.Smoke/KitchenInventory.Smoke.csproj -c Release --no-build
- Headless Desktop: set INVENTORY_HEADLESS=1 and run the Desktop exe with --headless

Artifacts (CI)
- KitchenInventory.Desktop-win-x64.zip
- diagnostics-bundle/*.zip
- InventoryApp-logs/
- crash-dumps/
- test-results/*.trx

Project layout
- src/KitchenInventory.Desktop: WPF app (Sentry, diagnostics, headless)
- src/KitchenInventory.Data: EF Core + migrations (SQLite)
- src/KitchenInventory.Smoke: DB migration + basic query
- tests/*: unit + UI smoke tests
- .github/workflows/dotnet.yml: CI jobs (build-test, crash-test)