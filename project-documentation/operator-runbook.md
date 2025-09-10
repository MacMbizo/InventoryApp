# Kitchen Inventory – Operator Runbook (Headless/CSV/Logs/Exit Codes)

Purpose
- This document enables operators to run Kitchen Inventory in non-interactive (headless) mode for automation, validate success via exit codes, import items from CSV, and collect logs/diagnostics for support.

Audience
- IT/Ops engineers, CI maintainers, and support staff deploying and running the Windows desktop application in scheduled or automated contexts.

Supported environment
- Windows 10/11
- Installed via MSI or run from a “publish” folder

Executable
- The application is “KitchenInventory.Desktop.exe”. If installed via MSI, it’s under Program Files (default vendor folder created by the installer). If running from a publish folder, use the exe path within that folder.

Key features (headless/automation)
- Headless startup check: verifies DB initialization/migration and exits without showing the UI.
- CSV import: ingest items from a CSV file, performing inserts/updates in a transaction with stock movement audit.
- Diagnostics export: produce a zip bundle with logs/config/db metadata for support.
- Crash test (for QA only): simulate crashes to verify telemetry and dump collection.

Exit codes
- 0: Success
- 1: General error
- 2: Invalid arguments
- 10: Database error
- 11: File operation error (e.g., missing import file)
- 20: Export error (diagnostics export failed)
- 21: Import error (CSV import failed)
- 30: Configuration error

Logs and diagnostics
- Logs directory: %LOCALAPPDATA%\InventoryApp\logs\
  - Rolling text files: log-YYYYMMDD.txt (file pattern is log-.txt with daily rolling)
- Database (SQLite default): %LOCALAPPDATA%\InventoryApp\kitchen.db (WAL enabled)
- Diagnostics export (zip): by default saved to %LOCALAPPDATA%\InventoryApp\ diagnostics-{version}-{commit8}-{utcTimestamp}.zip when no explicit path is provided

Database configuration (optional)
- Provider selection precedence: appsettings.json → INVENTORY_DB_PROVIDER env var → default Sqlite
  - INVENTORY_DB_PROVIDER = "Sqlite" or "Npgsql"
- Connection string:
  - Sqlite default: Data Source=%LOCALAPPDATA%\InventoryApp\kitchen.db;Cache=Shared;Pooling=True
  - Npgsql: set ConnectionStrings:KitchenDb in config or INVENTORY_DB_CONNECTION env var
- On Sqlite, the app enables WAL mode and sensible PRAGMAs at startup

Headless mode (no UI)
- Command-line flag: --headless
- Environment variables that also trigger headless:
  - INVENTORY_HEADLESS=1
  - CI=true (common in CI systems)
- Behavior: application starts, initializes DB/migrations, logs success, then exits with code 0.

CSV import (headless)
- Triggers (precedence: CLI then env):
  - --import-csv=path OR --import-csv path
  - --import-items=path OR --import-items path
  - INVENTORY_IMPORT_CSV=path
- Behavior:
  - Missing file → exit 11 (File operation error)
  - Empty file → treated as no-op success (exit 0)
  - Non-empty file → parsed and applied transactionally; items inserted/updated; stock movements recorded for quantity changes; exit 0 on success
  - Any exception during import → exit 21 (Import error)
- CSV schema (columns are case-insensitive; all optional, but Name is required to create/update meaningfully):
  - Id (int)
  - Name (string)
  - Quantity (decimal, invariant culture, e.g., 1.5)
  - Unit (string)
  - CategoryId (int) or CategoryName (string; matched case-insensitively to existing categories)
  - ExpiryDate (date in yyyy-MM-dd, UTC normalized)
  - CreatedAtUtc (ISO/date; parsed as UTC)
  - UpdatedAtUtc (ISO/date; parsed as UTC)
- Matching logic and updates:
  - If Id present and matches an existing item → update
  - Else if Name matches existing (case-insensitive) → update
  - Else → new item inserted
  - Quantity delta creates StockMovement entries (Add or Consume)
- CSV parsing rules:
  - Comma-separated, "quoted" fields supported, with standard double-quote escaping ("")

Items export (headless)
- Triggers (precedence: CLI then env):
  - --export-items[=path] OR --export-items path
  - --export-items-csv[=path] OR --export-items-csv path
  - INVENTORY_EXPORT_ITEMS=path OR INVENTORY_EXPORT_ITEMS_CSV=path (also supports 1/true to use default path)
- Behavior:
  - If no path is provided, exports to %LOCALAPPDATA%\InventoryApp\items-YYYYMMDD-HHMMSS.csv
  - On success → exit 0
  - On unauthorized/I-O errors → exit 11 (File operation error)
  - On other exceptions → exit 20 (Export error)
- CSV contents:
  - Matches application’s CsvExportService.ExportItems output; includes item attributes for all rows currently in the database
  - CSV schema preview (header row exactly):

```text
Id,Name,Quantity,Unit,CategoryId,CategoryName,ExpiryDate,CreatedAtUtc,UpdatedAtUtc
```

Recent movements export (headless)
- Triggers (precedence: CLI then env):
  - --export-movements[=path] OR --export-movements path
  - --export-movements-csv[=path] OR --export-movements-csv path
  - INVENTORY_EXPORT_MOVEMENTS=path OR INVENTORY_EXPORT_MOVEMENTS_CSV=path (also supports 1/true to use default path)
- Count control (optional):
  - Default: 50 most recent movements
  - CLI: --movements-take=NN or --take=NN
  - ENV: INVENTORY_EXPORT_MOVEMENTS_TAKE=NN
- Behavior:
  - If no path is provided, exports to %LOCALAPPDATA%\InventoryApp\movements-YYYYMMDD-HHMMSS.csv
  - On success → exit 0
  - On unauthorized/I-O errors → exit 11 (File operation error)
  - On other exceptions → exit 20 (Export error)
- CSV contents:
  - Matches application’s CsvExportService.ExportMovements output; includes recent stock movement entries with item name resolved when available
  - CSV schema preview (header row exactly):

```text
Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc
```

Diagnostics export (optional)
- Triggers (precedence: CLI then env):
  - --export-diagnostics=path OR --export-diagnostics (uses default name under %LOCALAPPDATA%\InventoryApp if path omitted)
  - INVENTORY_EXPORT_DIAGNOSTICS=path
- Behavior:
  - On success → exit 0
  - On failure → exit 20 (Export error)
- If both crash-test and diagnostics are requested, diagnostics takes precedence and crash-test is skipped

Crash test (for QA only)
- Triggers (precedence: CLI then env):
  - --crash-test[=sentry|dump]
  - INVENTORY_CRASH_TEST=1/true (defaults to “sentry”) or “dump”
- Behavior: throws or Environment.FailFast depending on mode; in automation contexts expect non-zero exit code

Examples (PowerShell)
- Headless check only (no UI):
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --headless; $LASTEXITCODE
- CSV import success:
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --import-csv "C:\\data\\items.csv"; $LASTEXITCODE  # expect 0
- CSV import missing file (negative test):
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --import-csv "C:\\data\\missing.csv"; $LASTEXITCODE  # expect 11
- Items export with default path:
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --export-items; $LASTEXITCODE  # writes items-YYYYMMDD-HHMMSS.csv under %LOCALAPPDATA%\InventoryApp
- Items export with explicit path:
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --export-items "C:\\temp\\items.csv"; $LASTEXITCODE  # expect 0
- Movements export with default path (50 most recent):
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --export-movements; $LASTEXITCODE  # writes movements-YYYYMMDD-HHMMSS.csv under %LOCALAPPDATA%\InventoryApp
- Movements export with explicit path and 100 entries:
  - & "C:\\Path\\To\\KitchenInventory.Desktop.exe" --export-movements "C:\\temp\\movements.csv" --movements-take 100; $LASTEXITCODE  # expect 0
- Using environment variables:
  - $env:INVENTORY_IMPORT_CSV = "C:\\data\\items.csv"; & "...\\KitchenInventory.Desktop.exe"; $LASTEXITCODE
  - $env:INVENTORY_HEADLESS = "1"; & "...\\KitchenInventory.Desktop.exe"; $LASTEXITCODE
  - $env:INVENTORY_EXPORT_DIAGNOSTICS = "C:\\temp\\diag.zip"; & "...\\KitchenInventory.Desktop.exe"; $LASTEXITCODE
  - $env:INVENTORY_EXPORT_ITEMS = "C:\\temp\\items.csv"; & "...\\KitchenInventory.Desktop.exe"; $LASTEXITCODE
  - $env:INVENTORY_EXPORT_MOVEMENTS = "C:\\temp\\movements.csv"; $env:INVENTORY_EXPORT_MOVEMENTS_TAKE = "200"; & "...\\KitchenInventory.Desktop.exe"; $LASTEXITCODE

Operational tips
- Always check $LASTEXITCODE in automation to determine success/failure
- Review %LOCALAPPDATA%\InventoryApp\logs\ for detailed errors
- For Sqlite, back up %LOCALAPPDATA%\InventoryApp\kitchen.db before large imports
- Use the included sample-import.csv in the repository root as a template

Troubleshooting
- Exit 11 (File operation error): verify import path, permissions, and that the file exists
- Exit 21 (Import error): review logs for parse/validation exceptions; check CSV encoding and date/number formats (invariant culture)
- Exit 10/30 (Database/Configuration): confirm provider/connection settings; default to Sqlite if Npgsql variables are not provided
- Empty import yields exit 0 by design; ensure the CSV has data rows beyond the header

CI recipes (Windows runners)
- Purpose: repeatable headless operations with exit code checks and artifact validation.

Example 1: Export Items and Movements with header verification
```powershell
$exe = "$env:LOCALAPPDATA\KitchenInventory\KitchenInventory.Desktop.exe"
$artifacts = Join-Path $PWD 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

$env:CI = 'true'
$env:INVENTORY_HEADLESS = '1'

$itemsCsv = Join-Path $artifacts 'items.csv'
& $exe --export-items "$itemsCsv"
if ($LASTEXITCODE -ne 0) { throw "Items export failed with code $LASTEXITCODE" }
if ((Get-Content -Path $itemsCsv -TotalCount 1) -ne 'Id,Name,Quantity,Unit,CategoryId,CategoryName,ExpiryDate,CreatedAtUtc,UpdatedAtUtc') { throw 'Items header mismatch' }

$movCsv = Join-Path $artifacts 'movements.csv'
& $exe --export-movements "$movCsv" --movements-take 25
if ($LASTEXITCODE -ne 0) { throw "Movements export failed with code $LASTEXITCODE" }
if ((Get-Content -Path $movCsv -TotalCount 1) -ne 'Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc') { throw 'Movements header mismatch' }
```

Example 2: Diagnostics bundle with tools/headless-export.ps1
```powershell
$artifacts = Join-Path $PWD 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$zipPath = Join-Path $artifacts 'diagnostics.zip'

$exePath = "$env:LOCALAPPDATA\KitchenInventory\KitchenInventory.Desktop.exe"
$psArgs = @('-File', 'tools/headless-export.ps1', '-OutputZip', $zipPath)
if (Test-Path $exePath) { $psArgs += @('-ExePath', $exePath) }

$proc = Start-Process -FilePath 'powershell' -ArgumentList $psArgs -NoNewWindow -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "Diagnostics export failed with exit code $($proc.ExitCode)" }
# Optional: zip integrity check
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath); try { if ($zip.Entries.Count -lt 1) { throw 'Empty zip' } } finally { $zip.Dispose() }
```

Change reference
- CI validates headless start, import success, and missing-file exit code 11 in .github/workflows/dotnet.yml
- Exit codes are defined centrally in the application and enforced in headless flows

Contact
- For installer issues (MSI) or CI failures, contact the Desktop/Release Engineering team.