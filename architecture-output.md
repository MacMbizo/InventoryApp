# School Kitchen Inventory Management — Architecture Blueprint (MVP)

## Executive Summary
- Key Decisions: Local-first desktop app; Shared .NET Standard Core library; Windows WPF UI first, macOS SwiftUI later; SQLite with EF Core; MVVM; Argon2id/bcrypt hashing; per-item thresholds; org-level ExpiringSoonDays=7; daily backups to %AppData%/KitchenInventory/Backups with 14-day retention; password policy and lockout (5 attempts/15m → 15m lock); audit logging for dismissals.
- Tech Stack Rationale: SQLite for zero-ops reliability and speed offline; EF Core for migrations and portability; .NET Standard Core enables reuse across platforms; MVVM for clean separation and testability; UUIDs + timestamps future-proof sync.
- Components Overview: WindowsUI (WPF views/viewmodels); Core (Models, Repositories, Services, Business Rules); Data (SQLite file, EF migrations); Utilities (BackupService, AuditService, SettingsService, AuthPolicy); Background worker for Needs Attention.
- Constraints/Assumptions: Single-machine use; modest hardware; no external APIs (MVP); Windows rollout first; macOS follows with identical Core.

## For Backend Engineers (Core Library)
### Business Logic Organization
- Layers: Models → Repositories (CRUD) → Services (domain rules) → ViewModels (UI-facing).
- Concurrency: Optimistic via RowVersion on mutable tables; detect 0 affected rows and surface refresh-required error.

### Internal Service Contracts (selected)
- InventoryService
  - GetItems(filter: name?, categoryId?): Paged<ItemDto>
  - CreateItem(dto): ItemId
  - UpdateItem(dto, originalRowVersion): void
  - AdjustQuantity(itemId, delta, reason, userId): void (writes UsageHistory)
- CategoryService
  - ListCategories(): Category[]; Create/Update/Delete (admin only; name unique, case-insensitive)
- NotificationService
  - EvaluateNeedsAttention(now): NeedsAttentionItem[] (low stock, expiring within Settings.ExpiringSoonDays)
  - Dismiss(userId, itemId): void (per-user + audit)
- AuthService
  - SignIn(username, password): Session (enforces lockout)
  - CreateUser(username, password, role): UserId (admin only)
  - ChangePassword(userId, current?, new): void; Unlock(userId): void
- SettingsService
  - Get/Set org-level settings; seed defaults on first run
- BackupService
  - DailyBackupIfNeeded(); ManualBackup(path?); Restore(backupFile)
- AuditService
  - Record(eventType, entityId, userId, metadata)

### Error Handling & Validation
- Fluent validation in Services; map to Result<T>/DomainError for ViewModels.
- Standard errors: ValidationFailed, NotFound, ConcurrencyConflict, Unauthorized, LockoutActive.

### Database Schema (EF Core, SQLite)
- Items
  - Id (UUID, PK), Name (TEXT NOT NULL), Quantity (INT NOT NULL ≥0), ExpirationDate (DATE NULL), CategoryId (UUID FK), LowStockThreshold (INT NOT NULL ≥0, default 5), IsDeleted (BOOL default 0), CreatedAt (DATETIME), UpdatedAt (DATETIME), RowVersion (INTEGER as concurrency token)
  - Indexes: IX_Items_Name (LIKE), IX_Items_CategoryId, IX_Items_ExpirationDate, IX_Items_IsDeleted
- Categories
  - Id (UUID, PK), Name (TEXT NOT NULL UNIQUE CI), IsDeleted (BOOL), CreatedAt, UpdatedAt
- Users
  - Id (UUID, PK), Username (TEXT NOT NULL UNIQUE CI), PasswordHash (TEXT), Role (TEXT: Admin|Manager|Staff), FailedLoginCount (INT), LockoutUntil (DATETIME NULL), IsDeleted, CreatedAt, UpdatedAt
- UsageHistory
  - Id (UUID, PK), ItemId (UUID FK), UserId (UUID FK), ChangeDate (DATETIME NOT NULL), QuantityChange (INT NOT NULL), Reason (TEXT)
- Dismissals
  - Id (UUID, PK), UserId (UUID FK), ItemId (UUID FK), DismissedAt (DATETIME)
- AuditEvents
  - Id (UUID, PK), EventType (TEXT), EntityType (TEXT), EntityId (UUID), UserId (UUID), Timestamp (DATETIME), Metadata (JSON)

### Migrations & Seeding
- EF Core migrations applied on startup; create DB if absent.
- Seed: default settings (ExpiringSoonDays=7, Backup config, Password policy); optional starter categories.

## For Frontend Engineers (Windows WPF MVP)
### Architecture & State
- MVVM per view; ViewModels call Services; DI container for Core services.
- Navigation: MainWindow hosts tabs: Inventory, Needs Attention; Admin-only Users, Categories.
- Performance: Virtualized DataGrid; async service calls; debounce search; background evaluation on app startup.

### Views and ViewModels (selected)
- LoginWindow/LoginViewModel: username/password, lockout messaging.
- InventoryView/InventoryViewModel: grid with search, category filter, sort; low/expiring cues.
- ItemDetailView/ItemDetailViewModel: create/edit item; per-item LowStockThreshold; quantity adjust (records UsageHistory).
- NeedsAttentionView/NeedsAttentionViewModel: list, per-user dismiss, deep links to item.
- Admin: UsersView (CRUD with policy), CategoriesView (CRUD unique names).

### API Integration & Errors
- Services injected; map Result<T> to UI notifications; show concurrency refresh dialog on conflict; inline validation messages.

## For QA Engineers
- Component Boundaries: Services unit-tested; ViewModels tested with fake services; UI smoke tests on key flows.
- Edge Cases: Quantity underflow; duplicate category name (case-insensitive); expired items with null expiration; lockout timers; restore workflow confirmation.
- Performance Targets: Inventory list ≤1s; background check unblocks UI within 300ms perceived.
- Security Tests: Password policy enforcement; lockout after 5/15m; role-guarded UI and service calls.

## For Security Analysts
### AuthN/AuthZ Model
- Local user store; Argon2id/bcrypt with per-user salt; constant-time comparison; lockout after 5 attempts in 15 minutes; 15-minute duration; admin unlock.
- Roles: Admin (full), Manager (read + quantity adjust), Staff (read + quantity adjust limited UI). Guard in UI and Services.

### Data Protection
- At-rest: SQLite file local; backups to %AppData%/KitchenInventory/Backups; retain 14 days; integrity verified. Optional post-MVP backup encryption; key not stored in plaintext.
- In-transit: N/A (local-only MVP).
- Audit: Dismiss/override actions immutable; Admin-visible.

## Infrastructure & DevOps (Local App)
- Packaging: Windows installer (MSIX/MSI) with per-user data in %AppData%.
- Environments: Dev uses same SQLite schema; migrations versioned.
- Logging/Observability: Rolling file logs in %AppData%/KitchenInventory/Logs; include errors, audit events; opt-in debug.

## API/Data Contracts (Concise Schemas)
- ItemDto { id, name, quantity, expirationDate?, categoryId, lowStockThreshold, createdAt, updatedAt, rowVersion }
- Category { id, name, createdAt, updatedAt }
- User { id, username, role, createdAt, updatedAt }
- NeedsAttentionItem { itemId, reason: LowStock|ExpiringSoon, details }
- Result<T> { ok: bool, data?: T, error?: { code, message, fields? } }

## Risks & Mitigations
- Concurrency anomalies: mitigate with RowVersion + user-friendly refresh.
- Data loss: daily + manual backups; restore confirmation and restart.
- Performance on older hardware: virtualized grids, async IO, minimal allocations.
- Future sync: UUIDs/timestamps enable later push/pull service without schema changes.