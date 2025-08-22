# School Kitchen Inventory Management — Product Manager Output (MVP)

## Executive Summary
- Elevator Pitch: A simple, local‑first desktop app for Windows and macOS that helps school kitchens track inventory, prevent stockouts and waste, and control access with roles.
- Problem Statement: Kitchens rely on manual spreadsheets and ad‑hoc processes, causing stockouts, excess purchases, and food waste; staff need a fast, offline tool that works on older devices.
- Target Audience: K‑12 school kitchen roles — Admin (Kitchen Supervisor), Manager (Assistant/Lead), Staff (Line/Prep). Secondary: District Food Service Manager (future reporting).
- Unique Selling Proposition: Local‑first reliability (SQLite, offline), low learning curve, role‑based access, and clear “Needs Attention” signals; engineered for future sync and multi‑platform UI.
- Success Metrics: ≤1s inventory list load; 30% fewer stockouts in 60 days; 20% reduction in near‑expiry waste; <3 min end‑of‑day count; 90% weekly active use by Staff after 2 weeks.
- Rollout: Windows first; macOS later.

## Feature Specifications

### 1) Inventory List & Search/Filter (P0)
- User Story: As Staff, I want to quickly find items by name/category and sort them so I can update or verify stock fast.
- Acceptance Criteria:
  - Given items exist, when I type in the search box, then the list filters by Name in real time; category filter narrows results further.
  - Columns sortable by Name, Quantity, Expiration Date; empty state shown when no matches.
  - Visual cues for low stock (below threshold) and items expiring within N days.
- Priority: P0 (daily operational core).
- Dependencies: Items, Categories tables; thresholds per item.
- Technical Constraints: SQLite; MVVM; performant on Windows 10 era hardware.
- UX Considerations: Large, high‑contrast grid; keyboard navigation; responsive layout.

### 2) Item & Category Management (P0)
- User Story: As Admin, I manage categories; as Staff/Manager, I add/edit items and update quantities to keep stock accurate.
- Acceptance Criteria:
  - Create Item with Name (required), Quantity (non‑negative int), Category (required), Expiration Date (optional); soft‑delete hides items.
  - Update Quantity appends a UsageHistory record with timestamp, user, and reason.
  - Create Category requires unique Name; Admin‑only CRUD for categories.
  - LowStockThreshold defaults to 5 on item creation (editable per item; non‑negative integer; required for save).
- Priority: P0 (data integrity and maintenance).
- Dependencies: UsageHistory table; Roles; EF migrations.
- Technical Constraints: UUID keys; soft‑delete flags; timestamps (CreatedAt/UpdatedAt).
- UX Considerations: Minimal typing forms, date pickers, category dropdown, inline validation.

### 3) Item Tracking & Low Stock Notifications (P0)
- User Story: As Staff/Manager, I need a “Needs Attention” view so I can act on low stock or upcoming expirations.
- Acceptance Criteria:
  - Background check on app launch flags items below LowStockThreshold or expiring within 7 days (configurable default: ExpiringSoonDays = 7) and lists them in “Needs Attention.”
  - Dismissed items persist per user (not global) and an audit event is recorded (user, item, action, timestamp); items re‑enter the list when conditions still apply after subsequent checks.
- Priority: P0 (prevents stockouts/waste).
- Dependencies: Threshold per item; background task scheduler.
- Technical Constraints: Lightweight queries; no UI freeze.
- UX Considerations: Non‑blocking notifications, actionable links to item detail.

### 4) User & Role Management (P0)
- User Story: As a user, I log in securely; as Admin, I create/manage user accounts and roles.
- Acceptance Criteria:
  - Secure login validates username + password hash; unauthorized roles cannot access restricted views/actions.
  - Admin‑only user CRUD; Admin cannot delete own account or demote self.
  - Password policy (standard): minimum 12 characters; must include upper‑ and lower‑case letters, a number, and a special character; password confirmation on create/reset.
  - Account lockout: lock user after 5 failed attempts within a 15‑minute window; lockout duration 15 minutes; admin unlock/reset available; reset lockout on successful authentication.
- Priority: P0 (governance and safety).
- Dependencies: Users table; AuthenticationService; Roles constants.
- Technical Constraints: Hashing (Argon2/bcrypt), salt; local storage only.
- UX Considerations: Clear errors, disabled/hidden restricted UI, lockout messaging (if configured).

### 5) Rule‑Based Reorder Suggestions (P1 / Post‑MVP)
- User Story: As Admin/Manager, I want reorder suggestions based on consumption so I can buy the right quantities.
- Acceptance Criteria:
  - System calculates AvgDailyUse (last 30 days) from negative usage and estimates DaysRemaining; triggers suggestions when DaysRemaining < X or below threshold.
  - Suggests RecommendedOrder for Y days; allow manual override/dismiss.
- Dependencies: UsageHistory; SuggestionService.
- Constraints: Transparent, explainable logic; local execution.

### 6) Reporting Dashboard (P2 / Post‑MVP)
- User Story: As Manager/Admin, I view key metrics and export reports.
- Acceptance Criteria: Inventory value summary, near‑expiry report, item/category trends; CSV/PDF export; role‑restricted access.

### 7) Barcode Scanner Integration (P2 / Post‑MVP)
- User Story: As Staff, I scan an item to locate/update it instantly.
- Acceptance Criteria: Keyboard‑wedge scanner input recognized; barcode lookup focuses item; simple Scan Mode for rapid updates.

## Requirements

### Functional
- User Flows: Login → Main (Inventory) → Search/Filter → Item Detail → Update Qty; Admin → Manage Users/Categories.
- State Management: Current user session (Id, Role); view filters/sorts; pending notifications.
- Validation: Required fields, unique category name, non‑negative quantity; soft‑delete behavior; role checks on every sensitive action.
- Integration: Local SQLite via repositories; no external APIs in MVP.
- Backup & Restore: Automatic daily backup (at first app launch per day) and manual backup/restore via Admin UI; backup naming includes timestamp; restore prompts for confirmation and app restart.
- Audit Trail: Persist dismiss/override actions per user with userId, itemId, action type, and timestamp; immutable log accessible to Admins.
- Settings: Org‑level setting for ExpiringSoonDays (default 7) configurable by Admin; per‑item LowStockThreshold editable in item detail.

### Non‑Functional
- Performance: List load ≤1s on modest hardware; background checks under 300ms perceived impact.
- Scalability: Designed with UUIDs/timestamps for future sync; EF migrations for schema evolution.
- Security: Hashed+salted passwords (Argon2id or bcrypt); standard password policy (min 12 chars, complexity as above); account lockout (5 attempts/15 minutes → 15‑minute lock); least‑privilege UI; no plaintext secrets.
- Reliability & Data Protection: Store local backups at %AppData%/KitchenInventory/Backups on Windows (and ~/Library/Application Support/KitchenInventory/Backups on macOS post‑rollout); retain last 14 daily backups; validate backup integrity on create.
- Accessibility: High contrast, readable fonts, keyboard navigation.

### UX Requirements
- Information Architecture: Primary nav — Inventory, Needs Attention, (Admin) Users, (Admin) Categories; future Reports.
- Progressive Disclosure: Advanced actions behind role‑guarded buttons/menus.
- Error Prevention: Inline validation; confirmations for destructive actions; undo where feasible.
- Feedback: Toasts/snackbars for updates; empty states with guidance.

## Decisions
- Low Stock & Expiry Defaults: LowStockThreshold defaults to 5 per item (editable per item); ExpiringSoonDays default is 7 (org‑level setting).
- Dismissal Policy: Dismissals persist per user and are recorded to an audit trail (user, item, action, timestamp); items re‑appear when conditions still apply.
- Password Policy & Lockout: Minimum 12 characters with mixed character classes; lockout after 5 failed attempts in 15 minutes for 15 minutes; admin unlock/reset supported.
- Backup/Restore: Daily automatic backups to %AppData%/KitchenInventory/Backups (Windows); retain 14 days; manual backup/restore via Admin UI. macOS location to mirror under ~/Library/Application Support/KitchenInventory/Backups in subsequent release.
- Rollout Order: Windows first; macOS later.

## Validation & Success
- Definition of Done: All P0 criteria met; role enforcement verified; performance targets achieved; unit tests for business rules; UAT sign‑off by Kitchen Supervisor.
- Measurement Plan: Track flagged items resolved per week, stockout incidents, end‑of‑day processing time, and low‑stock lead time.

## Test Scenarios (MVP)

1) Inventory List & Search/Filter
- Given there are items named "Milk 2%" (Category: Dairy) and "Whole Wheat Bread" (Category: Bakery), when I type "milk" in the search box, then only "Milk 2%" remains visible.
- Given the category filter is set to Dairy, when I clear the search box, then only items in the Dairy category are visible.
- Given a search term is applied and a category is selected, when both conditions match an item, then it remains visible; otherwise it is hidden.
- Given the column header Name is clicked, when I click it again, then sorting toggles between ascending and descending.
- Given no items match the current filters, when the list renders, then an empty state message is displayed.
- Given an item has Quantity = 4 and LowStockThreshold = 5, when the list renders, then the item shows a low‑stock visual cue.
- Given an item expires in 3 days and ExpiringSoonDays = 7, when the list renders, then the item shows an expiring‑soon cue; if no Expiration Date, then no expiring cue appears.

2) Item & Category Management
- Given I create an item with Name, Category, Quantity, optional Expiration Date, when I save, then the item persists with CreatedAt/UpdatedAt timestamps.
- Given I create a new item and do not modify LowStockThreshold, when I save, then LowStockThreshold is set to the default 5; when I set a custom threshold (non‑negative int), then that value is saved.
- Given I update an item’s Quantity from 10 to 7, when I save, then a UsageHistory entry is created with userId, timestamp, and reason; similarly, increasing quantity also records an entry.
- Given I create a category with a name that already exists (case‑insensitive), when I save, then validation fails with a unique name error; when the name is unique, then save succeeds.
- Given I soft‑delete an item, when I view the inventory list, then the item is hidden by default; when I filter to include deleted (admin‑only), then it can be seen with its soft‑deleted state.

3) Item Tracking & Low Stock Notifications (Needs Attention)
- Given at app launch, when the background check runs, then items below LowStockThreshold or expiring within ExpiringSoonDays=7 populate the Needs Attention view.
- Given I dismiss an item from Needs Attention, when I sign out and another user signs in, then that item is not dismissed for the other user.
- Given I dismiss an item, when conditions still apply at the next background check, then the item re‑appears in Needs Attention; the prior dismiss is retained in the audit log.
- Given I dismiss an item, when I view the audit trail as an Admin, then I see userId, itemId, action=dismiss, and timestamp.

4) User & Role Management
- Given I enter a valid username and password, when I sign in, then I am authenticated and redirected to the Inventory view; unauthorized roles cannot access Admin views.
- Given I enter a password not meeting policy (length < 12 or missing required character classes), when I attempt to create/reset a user, then validation fails with clear guidance.
- Given I attempt 5 failed sign‑ins within 15 minutes, when I try again before the 15‑minute lockout duration expires, then I am blocked with a lockout message; after 15 minutes or admin unlock, sign‑in is possible.
- Given I am the sole Admin user, when I attempt to delete my own account or demote my role, then the action is blocked with an explanatory message.

## UAT Sign‑off Checklist (MVP)
- Inventory List loads in ≤1 second with representative data volume on target hardware.
- Search and category filters work independently and together; empty state appears correctly.
- Low‑stock and expiring‑soon indicators render according to per‑item thresholds and ExpiringSoonDays=7.
- Create/Edit Item enforces required fields and data types; default LowStockThreshold=5 applied on create when unchanged.
- Quantity updates write UsageHistory with user, timestamp, reason for both increases and decreases.
- Category CRUD enforces unique names; non‑admins cannot manage categories.
- Needs Attention view populates at app launch; dismiss is per‑user; audit trail entries are recorded and visible to Admins.
- Authentication meets password complexity; lockout triggers at 5 failures/15 minutes and lasts 15 minutes; admin unlock/reset works.
- Role enforcement hides/disables restricted actions; Admin cannot delete/demote self.
- Daily automatic backup occurs on first launch per day; manual backup/restore works; restore prompts confirmation and restarts app; backups stored under %AppData%/KitchenInventory/Backups on Windows; retention of last 14 daily backups is enforced.

## Configuration Keys (Types, Defaults, Scope)
- Notifications.ExpiringSoonDays: int, default 7, scope=Org.
- Items.LowStockThreshold (per‑item field): int, default 5 on item creation, scope=Item.
- Security.Password.MinLength: int, default 12, scope=Org.
- Security.Password.RequireUpper: bool, default true, scope=Org.
- Security.Password.RequireLower: bool, default true, scope=Org.
- Security.Password.RequireDigit: bool, default true, scope=Org.
- Security.Password.RequireSpecial: bool, default true, scope=Org.
- Security.Lockout.MaxAttempts: int, default 5, scope=Org.
- Security.Lockout.WindowMinutes: int, default 15, scope=Org.
- Security.Lockout.DurationMinutes: int, default 15, scope=Org.
- Backups.Enabled: bool, default true, scope=Org.
- Backups.DailyEnabled: bool, default true, scope=Org.
- Backups.RetentionDays: int, default 14, scope=Org.
- Backups.Path.Windows: string, default %AppData%/KitchenInventory/Backups, scope=Org (platform‑specific).
- Backups.Path.macOS: string, default ~/Library/Application Support/KitchenInventory/Backups (applies in macOS release), scope=Org.
- Backups.Encrypt: bool, default false, scope=Org (post‑MVP optional).
- Backups.EncryptionKeyLocation: string/null, default null, scope=Org (post‑MVP; see plan below).
- Audit.Enabled: bool, default true, scope=Org (dismiss/override events recorded).

## Initial Settings Seed (MVP defaults)
- Notifications.ExpiringSoonDays = 7
- Security.Password.MinLength = 12
- Security.Password.RequireUpper = true
- Security.Password.RequireLower = true
- Security.Password.RequireDigit = true
- Security.Password.RequireSpecial = true
- Security.Lockout.MaxAttempts = 5
- Security.Lockout.WindowMinutes = 15
- Security.Lockout.DurationMinutes = 15
- Backups.Enabled = true
- Backups.DailyEnabled = true
- Backups.RetentionDays = 14
- Backups.Path.Windows = %AppData%/KitchenInventory/Backups
- Backups.Path.macOS = ~/Library/Application Support/KitchenInventory/Backups (for macOS release)
- Backups.Encrypt = false (post‑MVP optional)
- Audit.Enabled = true
- Per‑Item Default on Create: Items.LowStockThreshold = 5

## Backup Encryption (Optional, Post‑MVP)
- Default: Backups.Encrypt=false; when enabled, backups are encrypted at rest. Admin must supply/manage an encryption key.
- Key Management (near‑term): Allow manual entry or file‑based key; do not store the key in plaintext within app settings.
- Key Management (future): Integrate with OS keychain/credential vault (e.g., Windows DPAPI/Credential Manager; macOS Keychain) for secure at‑rest storage.
- Rotation: Support key rotation by decrypt‑then‑re‑encrypt existing backups; require admin confirmation and validation step.
- Recovery: Document recovery process when key is lost (backups unrecoverable without key); provide prominent warnings in UI when enabling encryption.