# Kitchen Inventory App - Development Phases & Progress

## Overview
This document tracks the implementation phases for the Kitchen Inventory Desktop application. Each phase builds incrementally toward a production-ready WPF application with MSIX packaging and enterprise-grade reliability.

## Phase 1: Usability and Data Integrity ✅ (COMPLETE)
*Enable users to efficiently manage inventory with data validation and intuitive UI.*

### Features
- ✅ **Search/Filter/Sort in Grid**: ICollectionView with real-time filtering by name/unit; sortable columns
- ✅ **Status Bar**: Item counts, filter status, last saved timestamp
- ✅ **Save Validation**: Save disabled when validation errors exist (implemented and verified by tests)
- ✅ **Culture-Aware Numeric Input**: Quantity entry validated against the current culture's decimal separator (`MainWindow.QuantityRegex`, `StockOperationDialog.QuantityRegex`) with culture-aware converters (`DoubleDecimalConverter`, `NeedsAttentionConverter`); covered by `CultureTests`
- ✅ **Transactional Stock Movements**: Audit ledger written in the same transaction as the item change (add/consume/adjust/delete/import) with timestamp and user; exposed via `StockOperationDialog` and the recent/selected movement panels
- ✅ **Timestamp Maintenance**: `CreatedAtUtc`/`UpdatedAtUtc` enforced centrally in `KitchenInventoryDbContext.SaveChanges`/`SaveChangesAsync` (delivered as a `SaveChanges` override rather than an EF interceptor) [reconciled 2026-10-24: was previously listed as an open "DbContext Interceptors" item]
- ✅ **Testing Suite**: Unit + integration tests (SQLite) plus a FlaUI UI suite running separately as `Category=UI`; 270 non-UI tests in the coverage-gated run

### Acceptance Criteria
- [x] User can search by name/unit with real-time filtering
- [x] User can sort by any column (click column headers)
- [x] Status bar shows item counts and filter status
- [x] Save button disabled when validation errors exist
- [x] Last saved timestamp displayed in status bar
- [x] Add/consume/adjust operations create movement records with timestamp/user/reason (written transactionally with the item change; verified by tests)
- [x] Current stock derived or kept consistent with audit trail (item quantity and its movement row commit in a single transaction)
- [x] Audit trail visible per item (stock operation dialog + recent/selected movement panels + Movement History report)

### Recent Changes
- **2026-10-24**: Reconciled status against the code — culture-aware quantity input, transactional movement writes and central timestamp maintenance are all implemented, so the previously open "culture formatting", "UI exposure pending" and "DbContext Interceptors" entries were retired
- **2025-09-12**: Improved UI test resilience (AutomationIds + fallback selectors); all UI tests for Add Item dialog passing
- **2025-09-12**: Diagnostics exporter (GUI + headless) verified; CI step exports bundle on failure
- **2025-09-12**: CSV import/export implemented and covered by tests; toolbar buttons wired
- **2024-12-27**: Refined UI layout with toolbar Grid (left actions, right search) and responsive DataGrid sizing
- **2024-12-27**: Added ICollectionView filtering, status bar with counts, search functionality
- **2024-12-27**: Initial MVVM implementation with search/filter capabilities

---

## Phase 2: Alerts and Catalogs 🚧 (IN PROGRESS)
*Organize inventory with categories, locations, and proactive alerts.*

### Features
- ✅ **Categories and Locations**: Item categories and a Locations catalog for organization; both manageable from the toolbar and applied as grid filters
- ✅ **Unit Catalog with Conversions**: Pure `UnitConversionService` (mass: mg/g/kg; volume: ml/cl/dl/l) with alias support and a "Unit Converter" dialog
- ✅ **Low Stock Thresholds**: Configurable threshold (default 5) with alert generation
- ✅ **Expiring Soon Notifications**: Days-to-expiry alerts with a background refresh timer (5 min) so alerts stay current across day boundaries
- ✅ **Alerts Pane with Badge Counts**: Dedicated "Alerts" tab listing low/out-of-stock, expiring and expired items plus Low/Expiring/Expired/Critical badge counts
- ✅ **CSV import/export with validation preview**: Import runs through `CsvImportPreviewDialog` + `CsvImportPreviewAnalyzer` with a per-row validation summary, invalid rows are skipped on confirm, and the issue list is downloadable as CSV (`BuildErrorsCsv`); items export includes location columns

### Acceptance Criteria
- [x] User can organize items by category and location
- [x] Unit conversions work automatically (e.g., 1.5 kg = 1500 g)
- [x] Low stock alerts appear when quantity falls below threshold
- [x] Expiring items highlighted with days-to-expiry
- [x] CSV export includes all item data (including location); import validates and shows preview
- [x] Import errors downloadable as CSV with issue descriptions

### Recent Changes
- **2026-10-24**: Reconciled status — the import preview and the downloadable error CSV are both implemented (`CsvImportPreviewDialog.OnExportErrorsClick`), so the "detailed error reporting pending" entry was retired
- **2026-10-24**: Added Location catalog end-to-end (entity, EF migration `AddLocation`, `LocationManagementDialog`, Items/AddItem VM wiring, CSV export/import/preview and headless paths); items CSV header now includes `LocationId,LocationName`
- **2026-10-24**: Added `UnitConversionService` (kg↔g, l↔ml with aliases) and a "Unit Converter" toolbar dialog
- **2026-10-24**: Added alerts pane with badge counts, `InventoryAlertService` evaluator, and a background refresh timer
- **2026-10-24**: Added a Coverlet-based code-coverage gate (80% line floor scoped to testable logic; XAML code-behind, EF migrations and generated code excluded). Threshold config lives in `tests/KitchenInventory.Desktop.Tests/KitchenInventory.Desktop.Tests.csproj` and is opt-in (`/p:CollectCoverage=true`), so a plain `dotnet test` stays green
- **2026-10-24**: Raised logic coverage from 48.9% to **85.4% line** (Domain 88.9%, Data 97.1%, Desktop 84.6%; 220 non-UI tests) via new suites for converters/validation/VersionInfo, `AddItemViewModel`/`AdminSetupViewModel`/`UnitConverterViewModel`, `DatabaseInfoService`, `DesignTimeDbContextFactory`, `PreferencesService`, `DiagnosticsExporter`, CSV import/preview services and `ItemsViewModel` (load, filter, save/delete movements, CSV import/export, RBAC gating)
- **2026-10-24**: Wired the coverage gate into CI (`.github/workflows/dotnet.yml`, `Test with coverage gate` step) so builds fail when logic line coverage drops below 80%

---

## Phase 3: Reporting and Packaging 🚧 (IN PROGRESS)
*Generate business reports and deploy as professional Windows application.*

### Features
- [x] **Reports**: Stock on hand, low stock, expiring soon, movement history
- [x] **Export Options**: CSV and PDF output for reports
- ✅ **MSI Packaging (WiX)**: `Installer.Msi` project with `Product.wxs` present in the solution
- 🚧 **MSIX Packaging**: `Installer.Msix.wapproj` + `Package.appxmanifest` exist, but local builds fail because the pinned SDK (`.dotnet/sdk/8.0.413`) lacks `Microsoft.DesktopBridge.props`; unsigned dev packaging is unverified and signing hooks are not started
- ❌ **App Distribution**: winget manifest for Microsoft Store or enterprise deployment
- ❌ **Update Channel**: In-app update notifications or MSIX incremental updates

### Acceptance Criteria
- [x] Generate and export stock reports to PDF/CSV
- [x] Installer projects (WiX MSI + MSIX) exist in the solution
- [ ] Build produces a verifiable, installable MSI package on a clean machine
- [ ] Build produces installable MSIX package (prerequisite: local `Microsoft.DesktopBridge.props` or a CI-only MSIX decision)
- [ ] App installs per-user under %LocalAppData%\InventoryApp
- [ ] App data persists across updates and uninstalls (unless "Reset" chosen)
- [ ] Users receive update notifications when new versions available

### Recent Changes
- **2026-10-24**: Added the reports engine and export pipeline: `ReportingService` (stock on hand, low stock, expiring soon, movement history) producing a WPF-free `ReportTable`, `ReportExporter` for CSV/plain-text/dependency-free PDF output, a `Reports` dialog (`ReportsWindow`/`ReportsViewModel`) with threshold, expiry-window and date-range inputs wired from preferences, and a Reports toolbar button + `_Reports` menu item. `IFileSaveService` gained `SaveBytesAsAsync` for binary PDF output; 47 new report tests (270 non-UI tests passing, 86.77% logic line coverage)

---

## Phase 4: Reliability and Observability ⚠️ (PARTIAL)
*Production-grade monitoring, support, and maintenance capabilities.*

### Features
- 🚧 **Crash Reporting**: Sentry integrated (DSN from config or `SENTRY_DSN`), opt-in consent preference, dispatcher/task/unhandled-exception handlers and an `INVENTORY_CRASH_TEST` hook — but no end-to-end sign-off run has been performed
- ✅ **Support Bundle Export**: Logs, config, and anonymized data for troubleshooting (GUI + headless implemented)
- 🚧 **Log Retention**: Serilog daily rolling file only (`rollingInterval: Day`); no retention count/size limit and no cleanup
- ❌ **Database Maintenance**: Startup PRAGMAs only (WAL, busy timeout, foreign keys, synchronous); no backup/restore, vacuum or integrity checks
- ❌ **Feature Flags**: Enable/disable beta features per user
- 🚧 **Telemetry**: Error/crash reporting only; no opt-in usage analytics

### Acceptance Criteria
- ⚠️ Crashes automatically generate support bundles (implementation present, unverified end-to-end)
- ✅ Users can export diagnostic data for support cases
- ❌ Database maintains performance with automatic maintenance
- ❌ Beta features can be enabled/disabled without code changes
- ❌ Telemetry respects user privacy preferences and GDPR

### Recent Changes
- **2026-10-24**: Reconciled status — crash reporting moved from "planned" to implemented-but-unverified (Sentry + consent preference + `INVENTORY_CRASH_TEST` hook); log retention narrowed to "daily rolling file, no retention limit"; database maintenance confirmed absent

---

## Phase 5: Security and Multi-User ⚠️ (IN PROGRESS)
*Enterprise security and collaboration features based on requirements.*

### Features
- ✅ **Authentication**: Local accounts with PBKDF2-SHA256 hashing, 5-attempt/15-min lockout; first-run Admin setup
- ✅ **Role-Based Access**: Admin/Manager/Staff permissions with UI gating; Admin-only user management dialog (create/edit role/reset password/unlock/delete)
- ❌ **Central Synchronization**: Sync to central database or API
- ❌ **Conflict Resolution**: Detect and resolve concurrent edit conflicts
- 🚧 **Audit Compliance**: Stock movement ledger provides change audit trail; full compliance logging pending

### Acceptance Criteria
- [x] Users authenticate before accessing application
- [x] Permissions control feature access (add/edit/delete/export)
- [ ] Multiple users can work with shared inventory data
- [ ] Conflicts resolved through merge strategies or user choice
- [ ] All actions logged for compliance and security audits

### Recent Changes
- **2026-10-24**: Added Admin-only User Management dialog (create user, change role, reset password, unlock, soft-delete) with self-modification and last-admin protections; RBAC-gated "Manage Users" menu item; 270 non-UI tests now pass in the coverage-gated run
- **2026-10-24**: Added local authentication (PBKDF2-SHA256, account lockout) and Admin/Manager/Staff role-based access control with login/first-run setup dialogs

---

## Roadmap (Prioritised)

**Guiding rule:** the app must be *run and exercised* before any packaging work. Packaging a build nobody has opened only moves defect discovery into the installer, where it is far more expensive.

### Gate 0 — Run and validate the current build (do this first)
- **Task**: Launch the WPF app against a scratch database and walk the real workflows end to end: first-run admin setup → sign-in → add/edit/delete item → consume/adjust via `StockOperationDialog` → category/location management → CSV import (valid **and** invalid rows, then download the error CSV) → CSV export → Alerts tab → Unit Converter → new **Reports** dialog (all four reports, CSV export, PDF export, open the PDF in a reader)
- **Task**: Run the existing FlaUI UI suite (`--filter "Category=UI"`), which currently covers launch/smoke, Add Item, category management and privacy consent
- **Acceptance**: Every workflow above completes with no unhandled exception in the log; the exported PDF opens; the ledger matches item quantities after a mixed session
- **Why first**: the 270 coverage-gated tests cover logic only. The Reports dialog in particular has never been opened, so its XAML wiring, DataGrid column generation and `SaveFileDialog` paths are unverified.

### Gate 1 — Product hardening (get to "happy with the app")
1. **Data integrity / audit reconciliation** — confirm item quantity and ledger sum agree after imports, deletes and stock operations; close the known architectural gap (soft-delete `IsDeleted` + `RowVersion` optimistic concurrency)
2. **Database maintenance** — backup/restore and integrity check; this database is the only copy of a school's inventory
3. **Crash reporting sign-off** — prove a real crash produces a Sentry event *and* a support bundle, with consent both on and off
4. **Log retention** — bound log growth (retained file count/size) before this runs unattended on a school machine
5. **Validation and locale polish** — quantity entry at boundaries (keyboard paste, fr-FR/de-DE separators)

### Gate 2 — Packaging (only after Gates 0–1)
6. MSIX package, unsigned dev build first, then signing hooks
7. MSI verification on a clean machine: install → use → upgrade → uninstall, with data persisting
8. Fix the local MSIX prerequisite (missing `Microsoft.DesktopBridge.props` in the pinned SDK) or formally declare MSIX CI-only

### Gate 3 — Distribution (last)
9. winget manifest
10. Update channel / in-app update notifications

### Explicitly deferred (not needed for v1)
- Central synchronisation and conflict resolution (Phase 5) — v1 scope is a single-machine install
- Feature flags and usage telemetry — revisit only if a beta programme appears

---

## Development Standards
- **.NET 8.0** LTS with WPF and Entity Framework Core
- **MVVM Pattern** with INotifyPropertyChanged and RelayCommand
- **Dependency Injection** via Microsoft.Extensions.DependencyInjection
- **Structured Logging** with Microsoft.Extensions.Logging
- **SQLite Database** with EF Core migrations
- **Git Conventional Commits** for clear change history
- **Build Validation** before each commit
- **Smoke Testing** after UI changes

## Version History
- **v0.1.0** (2024-12-27): Initial WPF app with CRUD operations
- **v0.2.0** (2024-12-27): Added search/filter with ICollectionView and status bar
- **v0.3.0** (2024-12-27): Refined UI layout with responsive toolbar and DataGrid sizing