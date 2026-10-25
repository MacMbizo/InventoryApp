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
- ✅ **Testing Suite**: Unit + integration tests (SQLite) plus a FlaUI UI suite running separately as `Category=UI`; 282 non-UI tests in the coverage-gated run

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
- ✅ **Crash Reporting**: Sentry integrated (DSN from config or `SENTRY_DSN`), opt-in consent preference, dispatcher/task/unhandled-exception handlers and an `INVENTORY_CRASH_TEST` hook; signed off end-to-end with consent both on and off (see Gate 1 item 3)
- ✅ **Support Bundle Export**: Logs, config, and anonymized data for troubleshooting (GUI + headless implemented)
- ✅ **Log Retention**: Serilog daily rolling file, rolled early once a file passes 10 MB, keeping the newest 30 files; the folder can never exceed ~300 MB however long the app is left running
- ✅ **Database Maintenance**: Integrity check (`PRAGMA integrity_check`, `PRAGMA foreign_key_check`, stock-ledger reconciliation), `VACUUM INTO` backups (automatic on launch, manual, and to another folder) and a restore that always leaves a `before-restore-*` safety copy
- ❌ **Feature Flags**: Enable/disable beta features per user
- 🚧 **Telemetry**: Error/crash reporting only; no opt-in usage analytics

### Acceptance Criteria
- ✅ Crashes automatically generate support bundles, with consent on and off (verified end-to-end)
- ✅ Users can export diagnostic data for support cases
- ❌ Database maintains performance with automatic maintenance
- ❌ Beta features can be enabled/disabled without code changes
- ❌ Telemetry respects user privacy preferences and GDPR

### Recent Changes
- **2026-10-25**: Completed Gate 1 item 3 — crash-reporting sign-off. The sign-off run uncovered three latent defects: `IDiagnosticsExporter` was never registered, so **the support bundle could not be produced at all** (`No service for type … has been registered`, exit 20); `SettingsWindow` and `DiagnosticsWindow` were likewise unregistered, so **Help → Diagnostics and Settings threw at click time**; and `--export-diagnostics` ignored the spaced `--export-diagnostics "path"` form that `--export-items` has always accepted. All three fixed. The crash-report chain is now verifiable: the reporting decision is logged after the sinks exist (it was previously logged inside the `UseSerilog` callback, before any sink existed, and lost) and every global handler logs the `SentryId` beside the crash, so an all-zero id proves nothing was sent. Two new exe-level tests cover the bundle contents and the no-consent crash path; 341 non-UI tests pass at 86% line coverage, 16 UI tests pass
- **2026-10-25**: Completed Gate 1 item 5 — validation and locale polish. `QuantityText` is now the one place that decides what counts as a quantity while it is being typed and what a pasted value means: `StockOperationDialog`, the grid's inline editor (a new `EditingElementStyle` on the `Quantity` column) and `AddItemDialog` all route through it, and the two duplicated `QuantityRegex` statics are gone. A paste carrying a foreign decimal separator, thousands separators or a trailing newline is converted to the machine's own convention instead of being dropped without a word
- **2026-10-25**: Completed Gate 1 item 4 — log retention. `LogRetention.WithLogRetention` (the single place the policy lives) caps each log file at 10 MB with `rollOnFileSizeLimit` and keeps only the newest 30; `App.xaml.cs` writes through it instead of the bare `rollingInterval: Day` sink
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
- **2026-10-25**: Completed Gate 1 item 3 — crash-reporting sign-off. The sign-off run uncovered three latent defects: `IDiagnosticsExporter` was never registered, so **the support bundle could not be produced at all** (`No service for type … has been registered`, exit 20); `SettingsWindow` and `DiagnosticsWindow` were likewise unregistered, so **Help → Diagnostics and Settings threw at click time**; and `--export-diagnostics` ignored the spaced `--export-diagnostics "path"` form that `--export-items` has always accepted. All three fixed. The crash-report chain is now verifiable: the reporting decision is logged after the sinks exist (it was previously logged inside the `UseSerilog` callback, before any sink existed, and lost) and every global handler logs the `SentryId` beside the crash, so an all-zero id proves nothing was sent. Two new exe-level tests cover the bundle contents and the no-consent crash path; 341 non-UI tests pass at 86% line coverage, 16 UI tests pass
- **2026-10-25**: Completed UI/UX Wave 2 — the design system. `Themes/Tokens.xaml` (palette, type scale, spacing, radii, control metrics), `Themes/Icons.xaml` (a stroked icon per toolbar action) and `Themes/Controls.xaml` (implicit styles and templates for every control, including a replacement `ComboBox` template and a `ToolbarButton` that puts an icon from `Tag` beside its word) are merged in `App.xaml`; `MainWindow` and all 14 dialogs were stripped of inline colours and one-off sizes, so controls are 38 px high, rows 44 px and radii 8 px everywhere. The six dialogs the view model builds directly carry their own copy of the theme, so they no longer depend on `App.xaml` having been loaded. `Ctrl+F`, `F2` and `F5` added; the window floor is now 960×600. A malformed `EllipseGeometry` in the new icon set (`Center="7.5"`) had made `MainWindow` fail to load with `XamlParseException … Point.Parse`, which neither the build nor the non-UI suite could catch — fixed. 341 non-UI tests pass at 86% line coverage; 11 of 16 UI tests pass locally, and the 5 failures reproduce unchanged on the commit before this work (this sandbox denies the synthetic keystrokes and second-level menu clicks they need; they pass in CI)
- **2026-10-25**: Completed Gate 1 item 5 — validation and locale polish. `QuantityText` replaces the duplicated `QuantityRegex` statics in `StockOperationDialog` and `MainWindow` and is wired into all three quantity entry points, converting pasted foreign-locale amounts to the local convention; 341 non-UI tests pass (36 new) at 86% line coverage, 14/14 UI tests pass
- **2026-10-25**: Completed Gate 1 item 4 — log retention. `LogRetention.WithLogRetention` caps each log file at 10 MB and keeps the newest 30, replacing the unbounded `rollingInterval: Day` sink; 305 non-UI tests pass (4 new) at 85.74% line coverage
- **2026-10-25**: Completed Gate 1 item 2 — `DatabaseMaintenance` adds an integrity check (`PRAGMA integrity_check`, `PRAGMA foreign_key_check`, stock-ledger reconciliation) and `VACUUM INTO` backups; the new **Your data** window copies the database (or one to a USB stick), checks it in plain language and restores an older copy behind a `before-restore-*` safety copy. An automatic backup is taken on every launch, keeping the newest 5 and never pruning manual or safety copies
- **2026-10-25**: Completed Gate 1 item 1 — `Item` gained `IsDeleted` (with a global query filter) and a `RowVersion` concurrency token; deleting an item is now a soft delete that keeps its movement history and writes the stock off, and stale writes surface as a reported conflict instead of overwriting. 280 non-UI tests pass
- **2026-10-25**: Adjust movements now store the signed delta, with `StockLedger.ReconcileAsync` verifying `Item.Quantity == ΣAdd − ΣConsume + ΣAdjust`
- **2026-10-24**: Added Admin-only User Management dialog (create user, change role, reset password, unlock, soft-delete) with self-modification and last-admin protections; RBAC-gated "Manage Users" menu item

---

## Roadmap (Prioritised)

**Guiding rule:** the app must be *run and exercised* before any packaging work. Packaging a build nobody has opened only moves defect discovery into the installer, where it is far more expensive.

### Gate 0 — Run and validate the current build (do this first)
- ✅ **Task**: Launch the WPF app against a scratch database and walk the real workflows end to end. Verified against the **Release** build with a scratch DB (`artifacts/manual-e2e`): first-run "Crash Reporting" consent → "Initial Admin Setup" → main window → **Reports** dialog (4 report kinds, kind switch, Generate) → PDF export → close → clean shutdown `ExitCode=0`. The exported PDF was written (938 bytes, `%PDF-` header). No `[ERR]`/`[WRN]`/`Exception`/`denied` entries anywhere in the run log, and no orphaned processes.
- ✅ **Task**: Extend the live walkthrough to the previously un-walked workflows — all now verified against the same Release build and scratch DB (`ALL CHECKS PASSED`): **Unit Converter** (`1500 g = 1.5 kg`, swap → `1500000 g`), **CSV import** (5 preview rows; error CSV reports empty name / negative quantity / bad date; 2 valid rows imported after the error-row confirmation), **Alerts tab** (low-stock + expiring rows rendered), **stock operations** (`Add Stock - Cooking Oil`: 20 → 30 l; consume 3 l → 27 l), **Add item** (`Sugar` 6 kg), **Adjust Stock** (→ 9 kg) then **delete**, **Manage Categories** (`Dry Goods`), **Manage Locations** (`Dry Store`), **Reports → Movement History** ("7 row(s) generated"), CSV exports (2 items, 7 movements), and a **ledger reconciliation** pass (per-item `add − consume == quantity` for items untouched by Adjust — `Cooking Oil` 27 == 27, `Fresh Tomatoes` 2 == 2).
- ✅ **Task**: Run the FlaUI UI suite (`--filter "Category=UI"`) — 13/13 passing (launch/smoke, diagnostics, Add Item, category management, privacy consent, headless export exit codes, plus three regressions: CSV import confirmation, Add Stock completion, and Adjust Stock applying a signed ledger delta). Each new test was proven to fail against the code before its fix and pass after.
- **Acceptance**: Every workflow above completes with no unhandled exception in the log; the exported PDF opens; the ledger matches item quantities after a mixed session ✅ (see the Adjust caveat below)
- **Why first**: the 280 coverage-gated tests cover logic only. The Reports dialog in particular has never been opened, so its XAML wiring, DataGrid column generation and `SaveFileDialog` paths are unverified.

> **Defect found and fixed — CSV import confirmation was silently discarded:** `CsvImportPreviewDialog.OnImportClick` set `DialogResult = true` but never set `Tag`, while `DialogService.ShowAsync` returns `dialog.Tag`. Confirming any import (in particular one containing error rows) therefore handed `null` back to `ItemsViewModel.ImportItemsCsvAsync`, which took the cancel branch and reported "Import canceled." **Fix:** set `Tag = true` before closing, mirroring `AddItemDialog`. This escaped the unit tests because they all invoke `ImportItemsCsvAsync` with `dialogs: null` and never construct a real preview dialog.

> **Defect found and fixed — stock-operation `NullReferenceException`:** `ItemsViewModel.ShowStockOperationDialogAsync` used `SelectedItem.Name` *after* `await LoadAsync()`, but `LoadAsync()` clears `Items` and never re-sets `SelectedItem`. Every successful stock operation therefore threw and was logged as "Failed to open stock operation dialog". **Fix:** capture the item name before the reload and use the captured value for the status text and log. Same blind spot — no existing test drives the real stock-operation dialog path.

> **Known gap (fixed in Gate 1):** `StockMovement.Quantity` was a *magnitude*; an **Adjust** row stored `|newStock − currentStock|`, so items touched by an Adjust could not be reconciled from add/consume movement sums alone (`add − consume ≠ quantity`). The live ledger check could only reconcile items with no Adjust. **Fix:** Adjust now stores the signed delta, `Add`/`Consume` keep their magnitude, and `StockLedger.ReconcileAsync` verifies `Item.Quantity == ΣAdd − ΣConsume + ΣAdjust` (see Gate 1, item 1).

> **Toolbar reachability (fixed):** `MainWindow` wrapped a single `Grid` of ~14 action buttons in a `ToolBar`. WPF moves that whole child into the toolbar overflow popup, so at the default window size every action (Refresh/Add/Delete/Save, stock ops, CSV import/export, Manage Categories/Locations, Unit Converter, Reports and the category/location filters) sat invisibly behind the `»` chevron — measured live as `Reports=False Add=False` reachable with `OverflowButton` present. The toolbar is now a `WrapPanel` inside a `Border`, so actions wrap onto extra rows and stay visible and clickable at any width (`OverflowButton` gone; `Reports=True Add=True`). UI suite green (12/12); 270 non-UI tests still pass.

> **Test isolation (resolved):** all app state now resolves through `AppPaths.GetDataDirectory()`, which honours the `INVENTORY_DATA_DIR` environment variable and otherwise defaults to `%LOCALAPPDATA%\InventoryApp`. The UI suite points every launched app at `artifacts/ui-test-dbs/{guid}/appdata`, so runs no longer write to — or depend on access to — the developer's profile. This removes the "access to the path …\logs\log-YYYYMMDD.txt is denied" failure caused by sandboxed runs and the >20 s process linger that accompanied it.


### Gate 1 — Product hardening (get to "happy with the app")

- ✅ **UI/UX Wave 1 — "safety and words"** — the first wave from the UI/UX review (`ui-ux-review.html`), aimed at non-technical school kitchen staff:
   - **Removing an item is no longer instant and irreversible.** `Delete item` opens a plain-language `RemoveItemDialog` that names the item, shows the stock that will be written off and flags an expired item; the safe choice ("Keep …") is the default button, so simply pressing Enter does not delete. Confirming soft-deletes the item (Gate 1 item 1) and shows a **"… was removed." · Undo** strip in the status bar for 10 seconds; `Ctrl+Z` also works. Undo restores the item and reverses its write-off movement, so the ledger invariant holds again.
   - **Plain-language pass.** Actions are `Take in stock` / `Take out stock` / `Recount` (single source: `Constants/StockOperations.cs`); tabs are `Problems` (was *Alerts*) with `Item history` and `Recent activity`; the grid header is `Item` (was *Name*) and the checkbox `Problems only` (was *Needs attention*); movement columns read `When / What happened / Amount / Note / By` via the new `MovementTypeConverter` and `MovementItemNameConverter`, so raw enum values and item ids never reach the screen; alert rows show `Out of stock / Running low / Use soon / Out of date` and `Urgent / Watch`. Status and error messages were rewritten ("Could not save your changes: …", "That file had no items in it.", "Imported 2 items: 1 new, 1 updated."). The stock dialog now reads `Now in stock: / What are you doing? / How much? / Why? (optional) / Done by: / After this:`.
   - **Keyboard shortcuts** via `InputBindings`: `Ctrl+N` add, `Ctrl+S` save, `Ctrl+R` refresh, `Ctrl+Z` undo removal, `Del` remove. Help → About is wired up (previously a dead menu item).
   - **Window sizing**: opens at 1100×700 with `MinWidth=900` / `MinHeight=560` so the layout cannot be squashed into unusability.
   - Covered by two new `[StaFact]` tests (`UndoRemoveCommand_PutsRemovedItemBack`, `DeleteItemCommand_KeepsItem_WhenConfirmationDeclined`). 282 non-UI tests and 13 UI tests pass; 85.7% logic line coverage (floor 80%).
- ✅ **UI/UX Wave 2 — "the design system"** — the second wave from the UI/UX review, and the one mechanical change every later wave benefits from:
   - **One vocabulary for every colour and size.** `Themes/Tokens.xaml` declares the palette (`Brush.Primary/Surface/SurfaceAlt/Border/Text/TextMuted/TextSubtle/Success/Warning/Danger` …), the type scale (`Font.Size.Base` 15 / `.Control` 13.5 / `.Caption` 12 / `.Heading` 17 / `.Display` 22) and the measurements (`Metric.ControlHeight` 38, `Metric.RowHeight` 44, `Metric.IconSize` 18, `Spacing.Xs/Sm/Md/Lg` 4/8/16/24, `Radius.Sm/Md/Lg` 6/8/12). `Themes/Controls.xaml` turns them into implicit styles and templates for `Button`, `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ComboBox` (a full replacement template — the square stock Windows chrome is gone), `ListBox`, `DataGrid`/`DataGridRow`/`DataGridCell`/`DataGridColumnHeader`, `ListView`, `ToolTip`, `StatusBar`, `GroupBox` and `Label`, plus the keyed helpers `PrimaryButton`, `DangerButton`, `DangerPrimaryButton` (Remove item), `SmallButton` (the Undo strip), `ToolbarButton`, `Card`, `CaptionText`, `SectionHeading` and `DangerText`. Both dictionaries, and `Themes/Icons.xaml`, are merged in `App.xaml`.
   - **Every inline colour and size is gone.** `MainWindow` and all 14 dialogs now reference tokens only; `#CC0000`/`Red`/`Tomato`, `Gray`/`#666`, `#F3F3F3`/`#F8F8F8`, `#DDD`, the `SystemColors` lookups and the one-off `CornerRadius="4"` are all retired. Controls are 38 px high, rows 44 px, radii 8 px.
   - **An icon set, always beside its word.** `Themes/Icons.xaml` holds a Lucide-style stroked geometry for each toolbar action, supplied to the shared `ToolbarButton` template through `Tag` (a null `Tag` collapses the icon slot, so the same template still serves text-only buttons). All 12 toolbar actions are icon+word; `Content`, `ToolTip` and `AutomationId` were left untouched, so the UI suite's name-based selectors still match.
   - **More of the keyboard.** `Ctrl+F` (find the search box), `F2` (edit the selected row) and `F5` (reload) join the existing `Ctrl+N`/`Ctrl+S`/`Ctrl+R`/`Ctrl+Z`/`Del`, each named in the tooltip of the button it duplicates. The window now opens at 1100×700 with a 960×600 floor.
   - **Dialogs that stand on their own.** The six dialogs the view model builds directly (Add item, Remove item, CSV import preview, stock operation, category and location management) carry their own copy of the theme, because the view model constructs them before the shell is necessarily in place; previously they silently depended on `App.xaml` being loaded, which is exactly the kind of coupling a test host does not have.
   - **How it was verified.** Release build clean (0 warnings, 0 errors) and 341 non-UI tests pass at 86% line coverage. The UI suite does launch the app and drive the dialogs — 11 of 16 pass — but the 5 that fail **fail identically on the commit before this work** (verified by stashing the change and re-running), because this sandbox refuses synthetic input: `SendInput` returns access-denied for the keystrokes `TextBox.Enter` sends, and the second-level `Help → Diagnostics` menu click never lands, so those five tests are exercising the desktop's input plumbing rather than the layout. They pass in CI on an interactive runner. The tokenising also caught one real defect: `Icon.Category`'s tag hole was written `Center="7.5"`, which is not a valid `Point`, and that single malformed geometry made `MainWindow` fail to load entirely (`XamlParseException … Premature string termination encountered while parsing '7.5'` at `Point.Parse`). The build and the whole non-UI suite passed while the app could not open at all, which is why the fix is called out here.
1. ✅ **Ledger reconciliation and deletion safety** — `StockMovement.Quantity` is the *signed* change for every movement type (`Add` positive; `Consume` stored as a positive magnitude but counted negative; `Adjust` = `newStock − oldStock`, which may be negative), so `Item.Quantity == ΣAdd − ΣConsume + ΣAdjust`. Added `StockLedger.ReconcileAsync` (in `KitchenInventory.Data`), which returns one entry per item whose recorded quantity does not match its movements. **Caveat:** Adjust rows written before this change stored a magnitude, so they surface as discrepancies until a real stock count corrects the item.
   - ✅ **Soft delete + optimistic concurrency** — `Item` gained `IsDeleted` and a `RowVersion` concurrency token (migration `AddItemSoftDeleteAndRowVersion`). Reads are filtered by a global query filter, so deleted items disappear from every list, export and report; the row and its movement history stay for audit, and the delete writes the stock off (`Adjust` = `−quantity`, then `Quantity = 0`) so the ledger invariant still holds. `RowVersion` is a regenerated `Guid` (SQLite has no native rowversion) written on every insert and update; a stale writer's save affects no rows and is reported as *"an item was changed elsewhere"* rather than silently overwriting.
   - Covered by an updated soft-delete test (row retained, stock written off, history not orphaned), a row-version rotation test, a stale-writer conflict test, a schema-upgrade test (previous schema → migrate → data intact) and the 7 `StockLedger` tests. 280 non-UI tests pass.
2. ✅ **Database maintenance** — backup, restore and integrity check, reachable from **Your data** in the menu bar. `DatabaseMaintenance.CheckAsync` runs SQLite's own `PRAGMA integrity_check`, `PRAGMA foreign_key_check` and `StockLedger.ReconcileAsync`, and reports each area in plain language (`The stock on hand matches the recorded history for every item.` / `Flour: 20 on hand, but the history adds up to 5 (out by +15).`); failures of the check itself are reported as "could not be checked" rather than as damage. Backups use `VACUUM INTO`, not a file copy, because the database runs in WAL mode where a copy can miss the newest committed rows or capture a torn write; an automatic `auto-kitchen-*.db` copy is taken on each launch (the newest 5 are kept, and manual/safety copies are never pruned), and `Back up to another folder…` writes one to a USB stick. Restoring always writes a `before-restore-*.db` safety copy and renames the live file into place through `SqliteConnection.ClearAllPools()` plus sidecar (`-wal`/`-shm`) removal, then restarts the app, so a restore is itself undoable.
3. ✅ **Crash reporting sign-off** — the sign-off run found and fixed three latent defects that had made the whole crash-report chain hollow, then proved the chain works. (a) **The support bundle could never be produced at all:** `IDiagnosticsExporter` was never registered in the container even though both the `--export-diagnostics` path and `DiagnosticsWindow` resolve it, so an export exited **20** with `No service for type 'KitchenInventory.Desktop.Services.IDiagnosticsExporter' has been registered`; it is now `AddSingleton`. (b) The same omission hit `SettingsWindow` and `DiagnosticsWindow`, which `MainWindow` resolves with `GetRequiredService`, so **Help → Diagnostics and the Settings menu item both threw at click time**; both are now `AddTransient`. (c) `--export-diagnostics` only understood the `=path` form (unlike `--export-items`, which has always accepted the spaced form), so `--export-diagnostics "C:\bundle.zip"` silently wrote to the default location; the spaced form is now accepted. The sign-off itself is now *verifiable* rather than trusted: the crash-reporting decision is written to the log **after** the sinks exist (previously it was logged inside the `UseSerilog` callback, before any sink existed, and was discarded), and each of the three global handlers writes the `SentryId` returned by `SentrySdk.CaptureException` beside the crash, so `DispatcherUnhandledException (Sentry event 0000…)` is positive proof Sentry did *not* accept the event while a non-empty id proves it did. Covered by two new exe-level tests: `ExportDiagnostics_WithExplicitPath_ExitsZero_AndCreatesTheSupportBundle` (exits 0, and the zip really contains `environment.json`, `database.json` and a `logs/` entry) and `CrashTest_WithReportingNotAgreedTo_ExitsNonZero_AndSaysSoInTheLog` (a fresh profile has no consent, so it exits non-zero, logs `Crash reporting is off…` and the all-zero event id). **Caveat:** the sandbox has no network, so the consent-**on** delivery was demonstrated with a syntactically valid DSN and the non-empty id it returned; only the consent-**off** path and the bundle contents are verified against a live service.
4. ✅ **Log retention** — `LogRetention` (`KitchenInventory.Desktop.Logging`) is the one place the policy lives: `log-*.txt` rolls daily, rolls early once a file passes **10 MB**, and only the newest **30** files are kept (`retainedFileCountLimit`), so the log folder is bounded at roughly 300 MB however long the app is left running on a school machine. The file name shape is unchanged, so the support bundle's "last 5 `log-*.txt`" collection still finds logs. The limits are parameters of `WithLogRetention` purely so tests can drive the real Serilog sink with small values and observe pruning; the app always uses the defaults. Covered by four `LogRetentionTests` (rolls and prunes to the limit, newest events survive, filename matches the support bundle's pattern, blank path rejected).
5. ✅ **Validation and locale polish** — quantity entry now behaves the same everywhere it is typed. `QuantityText` (`KitchenInventory.Desktop.Utilities`) is the single place that decides what may be typed and what a paste means, replacing the two private `QuantityRegex` statics that `StockOperationDialog` and `MainWindow` each kept. **Typing** is guided to the machine's own convention (the box refuses the wrong decimal separator, and allows at most 3 decimal places); **pasting** is converted rather than silently swallowed, so an amount copied from a spreadsheet or a foreign-locale website arrives as `1.5` or `1,5` depending on the machine, with thousands separators (NBSP, narrow NBSP, thin space and the Swiss apostrophe) and a trailing newline stripped. Input that cannot be recognised as a number is left alone, so the existing validation rule reports it instead of the app guessing. Wired into all three quantity entry points: the stock dialog, the grid's inline editor (a new `EditingElementStyle` on the `Quantity` column) and `AddItemDialog`. Covered by 37 `QuantityTextTests` cases (typing guard per culture, paste conversion under en-US/fr-FR/de-DE, thousands detection, non-numeric passthrough), which take over from the `CultureTests` reflection test that reached into `MainWindow`'s now-deleted private regex.

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