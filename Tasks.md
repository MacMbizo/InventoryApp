# School Kitchen Inventory Management — Tasks and Delivery Plan (MVP → Release)

Inputs referenced:
- Features: c:\InventoryApp\Features.md
- UI/UX Spec: c:\InventoryApp\kitchen-inventory-ui-ux-specification.md
- QA Plan: c:\InventoryApp\project-documentation\qa-output.md
- Delivery Plan (v1): c:\InventoryApp\Tasksv1.md
- Tests folder: c:\InventoryApp\tests

Legend: Priority = High/Med/Low, Est = calendar days, Dep = dependencies

A. Requirements & Planning (Est: 3–4 days)
- T-A1 (High, Est 1): Consolidate functional scope from Features (Items, Categories, Users, Suggestions) and UI/UX (roles, accessibility WCAG AA, navigation). Output: approved MVP scope and non-functional list (performance on older hardware, offline-first).
- T-A2 (High, Est 1): Define acceptance criteria per feature (CRUD, search/filter/sort, low-stock/expiring logic, role permissions). Dep: T-A1.
- T-A3 (Med, Est 1): Define UI standards from UI/UX spec (design tokens, focus states, keyboard flows, error patterns). Dep: T-A1.
- T-A4 (Med, Est 0.5): Risk/assumption register (local-first DB, future sync, barcode post-MVP).

B. Architecture & Project Setup (Est: 3–5 days)
- T-B1 (High, Est 0.5): Initialize solution structure per Tasksv1 Step 1 (Core lib, WindowsUI WPF, future MacUI placeholder). Deliverable: solution compiles.
- T-B2 (High, Est 1): Integrate EF Core + SQLite in Core; create KitchenDbContext; add entities (Item, Category, User, UsageHistory) and initial migration. Dep: T-B1.
- T-B3 (High, Est 0.5): Configure repository interfaces/implementations and DI wiring in Core. Dep: T-B2.
- T-B4 (High, Est 0.5): Add GitHub Actions CI (.github/workflows/dotnet.yml) running build + tests on PRs. Dep: T-B1.
- T-B5 (Med, Est 0.5): App configuration for local paths, logging, and versioning.

C. Authentication & Roles (Est: 3–4 days)
- T-C1 (High, Est 1): Implement AuthenticationService (hashing with Argon2/bcrypt), login flow, session state. Dep: T-B2.
- T-C2 (High, Est 1): Create Login UI (WPF) with accessibility and error states; ViewModel wiring (MVVM). Dep: T-C1, T-A3.
- T-C3 (High, Est 1): Role-based authorization (Admin/Manager/Staff) checks in services and view gating. Dep: T-C1.
- T-C4 (Med, Est 0.5): Seed default admin user; lockout policy and validation messages. Dep: T-C1.

D. Categories & Items CRUD (Est: 5–7 days)
- T-D1 (High, Est 1): CategoryService + Admin-only CategoryManagement view (unique name validation). Dep: T-C3.
- T-D2 (High, Est 2): InventoryService (create/update/read/soft-delete); UsageHistory write on quantity changes. Dep: T-B3.
- T-D3 (High, Est 1): Inventory list UI (grid): search, filter by category, sort; MVVM commands. Dep: T-D2, T-A3.
- T-D4 (Med, Est 1): ItemDetail modal (create/edit); validation and success/error patterns. Dep: T-D2.

E. Suggestions & Needs Attention (Est: 3–4 days)
- T-E1 (High, Est 1): SuggestionService: low stock threshold and forecast (7 days) per QA signals. Dep: T-D2.
- T-E2 (High, Est 1): Background check on startup/timer; remove suggestions for deleted items. Dep: T-E1.
- T-E3 (Med, Est 1): NeedsAttention view and visual cues in grid (warning/error badges). Dep: T-E1, T-D3.

F. Quality Assurance (Est: parallel across phases)
- T-F1 (High, Est 0.5): Adopt TDD loop per QA plan (Red/Green/Refactor). Ensure signals from QA report: LowStockThreshold=5, ExpiringSoonDays=7, Windows-first rollout appear in outputs/logs where applicable. Dep: A–E.
- T-F2 (High, Est ongoing): Keep QA report updated via QA agent after each milestone; verify headings/sections present. Dep: A–E.
- T-F3 (Med, Est 0.5): Static analysis/style rules and PR checklist.

G. Testing Strategy (Est: parallel across phases)
- T-G1 (High, Est 1): Unit tests for Core services (Auth, Inventory, Category, Suggestion). Target happy paths and edge cases (non-negative qty, unique category). Dep: T-B3.
- T-G2 (High, Est 1): Integration tests with in-memory/temporary SQLite to validate EF mappings and flows. Dep: T-B2.
- T-G3 (Med, Est 1): ViewModel tests (Inventory, ItemDetail, NeedsAttention, Users). Dep: C–E.
- T-G4 (Med, Est 0.5): Maintain QAAgentRunner tests; ensure CI passes on PRs. Dep: CI in T-B4.
- T-G5 (Med, Est 0.5): Test data builders and fixtures for reproducibility.

H. Packaging, Deployment, and Release (Est: 2–3 days)
- T-H1 (High, Est 1): Create Release build pipeline; artifacts per configuration; versioning scheme. Dep: T-B4.
- T-H2 (Med, Est 1): Windows packaging (MSIX or installer), optional code signing; release notes template. Dep: T-H1.
- T-H3 (Med, Est 0.5): Install/upgrade verification test checklist; smoke test script.

I. Post-Launch (Est: ongoing)
- T-I1 (Med): Bug triage SLA, issue templates, prioritization policy.
- T-I2 (Low): Telemetry-lite (opt-in logs), backup/restore guide.
- T-I3 (Low): Roadmap for barcode scanning and cloud sync.

J. Documentation (Est: parallel across phases)
- T-J1 (High, Est 0.5): Keep Tasksv1 plan aligned; update deltas and decisions log each milestone. Dep: A–E.
- T-J2 (Med, Est 0.5): Architecture and DB schema docs (entities, migrations). Dep: B.
- T-J3 (Med, Est 0.5): User/Admin guides (roles, permissions, backups). Dep: C–E.
- T-J4 (Med, Est 0.5): QA artifacts: test plan, coverage summary, and CI badges; link to QA report. Dep: F–G.

Milestone Timeline (indicative)
- M1 (Week 1): A + B complete, CI green, initial tests (G1 partial).
- M2 (Week 2): C complete; auth tests added; UAT of login/roles.
- M3 (Weeks 3–4): D complete; unit + integration tests for CRUD; inventory UI.
- M4 (Week 5): E complete; NeedsAttention UI; finalize tests (G1–G3).
- M5 (Week 6): H packaging + release candidate; docs (J), QA sign-off (F), smoke tests.

Quality Gates (from QA Plan)
- CI: dotnet build/test on every PR; all tests must pass.
- TDD cadence: new code requires unit tests; keep QA report sections present.
- Definition of Done: feature AC met, tests (unit/integration/VM) passing, accessibility checks from UI/UX spec, docs updated.

Traceability
- Requirements: Features.md + UI/UX spec → AC (T-A2) → Tests (G) → QA report checks (F).
- Non-functional: performance, accessibility, offline-first verified during UAT and smoke tests.

---

Cross-References to Source Documents and Acceptance Criteria

Requirements Analysis Synthesis (from Features.md and UI/UX Spec)
- Entities and Relations:
  - Item(Id UUID, Name, Quantity ≥ 0, ExpirationDate optional, CategoryId UUID FK, LowStockThreshold int, IsDeleted bool, CreatedAt, UpdatedAt)
  - Category(Id UUID, Name unique, IsDeleted, CreatedAt, UpdatedAt)
  - User(Id UUID, Username, PasswordHash, Role ∈ {Admin, Manager, Staff}, Lockout fields)
  - UsageHistory(ItemId, Delta, Reason, Timestamp)
- Functional flows:
  - CRUD for Items and Categories (Admin only for Category CRUD); soft delete for Items/Categories
  - Inventory list with search (LIKE on Name), filter (Category), sort (Name, Quantity, Expiration)
  - Low stock and expiring alerts; Needs Attention view; dismiss actions
  - Role-based permissions per Feature 2
- UI/UX non-functional:
  - WCAG AA contrast; visible 2px focus ring; keyboard navigation; ARIA labels for complex controls; 44×44 touch targets where applicable
  - Design tokens: Primary Blue #2563EB, hover #1D4ED8, Warning #D97706, Error #DC2626; typography scale (Display→Body), spacing (4px base)
  - MVVM pattern on Windows (WPF) with ViewModels wrapping Core services

Explicit Acceptance Criteria per Major Feature
- Authentication & Roles
  - Given valid credentials, when Sign In is clicked, then user is authenticated and session state (UserId, Role) is stored; invalid creds show error and preserve inputs except password
  - Admin can access UserManagement; Manager/Staff cannot; lockout after N failed attempts shows countdown
- Category Management (Admin only)
  - Create requires non-empty, unique Name; attempts to duplicate show inline error; list excludes IsDeleted=true
- Inventory Management
  - Create/Edit validates: Name non-empty, Quantity ≥ 0; changing Quantity creates UsageHistory entry; Delete performs soft delete; search/filter/sort operate client-side or DB-backed as designed; empty state and error banners follow spec
- Needs Attention & Suggestions
  - Items with Quantity < LowStockThreshold are flagged Warning; items with ExpirationDate ≤ today+7 are flagged Expiring; dismiss removes from view until next recomputation
- Accessibility
  - All interactive elements reachable by Tab/Shift+Tab order; focus ring visible; all icons paired with text; color not sole indicator; screen reader labels present for buttons, grid actions, and status badges

QA Procedures (from qa-output.md)
- Encode defaults LowStockThreshold=5 and ExpiringSoonDays=7 in configuration with single source of truth and surface in About/Diagnostics page
- Keep QA Agent report generation part of milestone reviews; verify Detected Sections present; ensure QA-derived TDD stages followed per feature
- CI/CD: dotnet build and dotnet test required on every PR; add quality gate to block merge on failing tests

Testing Strategy and Current Tests Directory Utilization
- Current state (discovered): c:\InventoryApp\tests\QAAgentRunner.Tests with ProcessingTests.cs (xUnit + FluentAssertions); 5 tests passing in pipeline
- Planned structure in tests directory:
  - c:\InventoryApp\tests\Core.Tests\ for unit tests of services (Auth, Inventory, Category, Suggestion)
  - c:\InventoryApp\tests\Integration.Tests\ for EF Core + SQLite integration tests (DbContext mappings, migrations, flows)
  - c:\InventoryApp\tests\WindowsUI.ViewModels.Tests\ for ViewModel tests (Inventory, ItemDetail, NeedsAttention, Users, Categories)
- Immediate tasks
  - Add Core.Tests with test data builders and fixtures; target high-priority invariants (non-negative qty, unique category, permission checks)
  - Add Integration.Tests using ephemeral SQLite files (or :memory:) validating CRUD flows and UsageHistory writes
  - Add ViewModels.Tests validating command enable/disable, search/filter logic, sorting, and badge visibility logic
  - Maintain and extend QAAgentRunner.Tests to assert presence of new headings/sections as QA report evolves

Dependencies and Sequencing (expanded)
- B (Architecture/Setup) unblocks C–E and G (tests)
- C (Auth/Roles) must precede Category Admin UI and restrict D (CRUD) capabilities
- D (CRUD) must precede E (Suggestions/Needs Attention)
- F/G (QA/Tests) run in parallel but require minimal scaffolding from B; CI from T-B4 is a prerequisite to enforce gates

Risk Register and Mitigations
- Performance on older hardware: use virtualization in long lists; avoid heavy synchronous operations on UI thread; background suggestion checks
- Accessibility regressions: add axe-like accessibility review checklist; manual screen reader pass during UAT
- Data integrity: enforce soft delete and referential integrity; migrations tested in Integration.Tests

Backlog and Stretch (post-MVP)
- Barcode scanning; Cloud sync; macOS SwiftUI parity; ReportingService with export; Backup/Restore automation

Measurement and Reporting
- Definition of Done: AC satisfied, unit/integration/VM tests passing in CI, accessibility checks complete, documentation updated (Tasksv1 and Architecture/DB schema), QA report updated
- Release readiness: M5 artifacts built, smoke test script passed, release notes published, installer verified on clean VM