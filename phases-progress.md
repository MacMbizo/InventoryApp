# Kitchen Inventory App - Development Phases & Progress

## Overview
This document tracks the implementation phases for the Kitchen Inventory Desktop application. Each phase builds incrementally toward a production-ready WPF application with MSIX packaging and enterprise-grade reliability.

## Phase 1: Usability and Data Integrity ✅ (IN PROGRESS)
*Enable users to efficiently manage inventory with data validation and intuitive UI.*

### Features
- ✅ **Search/Filter/Sort in Grid**: ICollectionView with real-time filtering by name/unit; sortable columns
- ✅ **Status Bar**: Item counts, filter status, last saved timestamp
- ✅ **Save Validation**: Save disabled when validation errors exist (implemented and verified by tests)
- ⚠️ **Culture-Aware Numeric Input**: Quantity input with regex validation (PARTIAL - needs culture formatting)
- 🚧 **Transactional Stock Movements**: Audit ledger model added and EF migration applied; UI exposure pending
- ❌ **DbContext Interceptors**: Enforce Created/Updated timestamps via SaveChanges override
- ✅ **Testing Suite**: Unit tests, integration tests (SQLite in-memory), UI smoke + automation tests; 39 tests passing in CI

### Acceptance Criteria
- [x] User can search by name/unit with real-time filtering
- [x] User can sort by any column (click column headers)
- [x] Status bar shows item counts and filter status
- [x] Save button disabled when validation errors exist
- [x] Last saved timestamp displayed in status bar
- [ ] Add/consume/adjust operations create movement records with timestamp/user/reason (model + migration done; runtime logic added; verify via DB)
- [ ] Current stock derived or kept consistent with audit trail
- [ ] Audit trail visible per item

### Recent Changes
- **2025-09-12**: Improved UI test resilience (AutomationIds + fallback selectors); all UI tests for Add Item dialog passing
- **2025-09-12**: Diagnostics exporter (GUI + headless) verified; CI step exports bundle on failure
- **2025-09-12**: CSV import/export implemented and covered by tests; toolbar buttons wired
- **2024-12-27**: Refined UI layout with toolbar Grid (left actions, right search) and responsive DataGrid sizing
- **2024-12-27**: Added ICollectionView filtering, status bar with counts, search functionality
- **2024-12-27**: Initial MVVM implementation with search/filter capabilities

---

## Phase 2: Alerts and Catalogs ❌ (PLANNED)
*Organize inventory with categories, locations, and proactive alerts.*

### Features
- Item categories and locations for organization
- Unit catalog with conversion rules (kg ↔ g, liters ↔ ml)
- Low stock thresholds with configurable alerts
- Expiring soon notifications with background timer
- Dedicated alerts pane with badge counts
- CSV import/export with validation preview and error reporting (PARTIAL: core import/export implemented with tests; preview + detailed error reporting pending)

### Acceptance Criteria
- User can organize items by category and location
- Unit conversions work automatically (e.g., 1.5 kg = 1500 g)
- Low stock alerts appear when quantity falls below threshold
- Expiring items highlighted with days-to-expiry
- CSV export includes all item data; import validates and shows preview
- Import errors downloadable as CSV with issue descriptions

---

## Phase 3: Reporting and Packaging ❌ (PLANNED)
*Generate business reports and deploy as professional Windows application.*

### Features
- **Reports**: Stock on hand, low stock, expiring soon, movement history
- **Export Options**: CSV and PDF output for reports
- **MSI Packaging (WiX)**: Dev/testing installer available today
- **MSIX Packaging**: Per-user installation with code signing hooks (planned)
- **App Distribution**: winget manifest for Microsoft Store or enterprise deployment
- **Update Channel**: In-app update notifications or MSIX incremental updates

### Acceptance Criteria
- Generate and export stock reports to PDF/CSV
- Build produces installable MSI package (current)
- Build produces installable MSIX package (target)
- App installs per-user under %LocalAppData%\InventoryApp
- App data persists across updates and uninstalls (unless "Reset" chosen)
- Users receive update notifications when new versions available

---

## Phase 4: Reliability and Observability ❌ (PLANNED)
*Production-grade monitoring, support, and maintenance capabilities.*

### Features
- **Crash Reporting**: Automatic crash dumps with user consent
- **Support Bundle Export**: Logs, config, and anonymized data for troubleshooting (GUI + headless implemented)
- **Log Retention**: Structured logging with rotation and cleanup
- **Database Maintenance**: Backup/restore, vacuum, integrity checks
- **Feature Flags**: Enable/disable beta features per user
- **Telemetry**: Opt-in usage analytics with privacy controls

### Acceptance Criteria
- Crashes automatically generate support bundles
- ✅ Users can export diagnostic data for support cases
- Database maintains performance with automatic maintenance
- Beta features can be enabled/disabled without code changes
- Telemetry respects user privacy preferences and GDPR

---

## Phase 5: Security and Multi-User ❌ (OPTIONAL)
*Enterprise security and collaboration features based on requirements.*

### Features
- **Authentication**: Local accounts or Active Directory/Azure AD integration
- **Role-Based Access**: Admin/User/ReadOnly permissions
- **Central Synchronization**: Sync to central database or API
- **Conflict Resolution**: Detect and resolve concurrent edit conflicts
- **Audit Compliance**: Full audit trails for regulatory requirements

### Acceptance Criteria
- Users authenticate before accessing application
- Permissions control feature access (add/edit/delete/export)
- Multiple users can work with shared inventory data
- Conflicts resolved through merge strategies or user choice
- All actions logged for compliance and security audits

---

## Next Actions (Immediate)
*Smallest shippable steps with clear acceptance criteria.*

### 1. Package as MSIX (Unsigned Dev)
- **Task**: Add MSIX project with manifest and dependencies
- **Task**: Configure build to produce .msix output
- **Acceptance**: Build produces installable MSIX file
- **Acceptance**: App installs per-user and persists data under %LocalAppData%

### 2. CSV Import Preview and Error Reporting
- **Task**: Add pre-commit import preview dialog with row-level validation summary
- **Task**: Provide downloadable error CSV with issue descriptions and row numbers
- **Acceptance**: Import shows preview with validation status before committing; error export works

### 3. Add Stock Movement Logic and UI
- **Task**: Implement runtime creation of StockMovement records on add/consume/adjust
- **Task**: Expose movement history in item details or dedicated view
- **Acceptance**: Each inventory change creates an audit record; history visible per item

### 4. Crash Reporting (Initial)
- **Task**: Integrate crash reporting (e.g., Windows Error Reporting + local dump, or Sentry)
- **Task**: Add opt-in consent and privacy toggle
- **Acceptance**: Crashes generate a bundle; user controls data sharing

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