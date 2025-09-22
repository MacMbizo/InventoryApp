# KitchenInventory Desktop – Development Rules and Process

Purpose
- Establish a consistent, production-minded process for Desktop (Windows-first, WPF, .NET 8) that is secure, observable, and maintainable.
- Govern day-to-day development, testing, CI/CD, packaging, and documentation.
- Keep phases-progress.md as the single source of truth for roadmap and status; this file defines how we work.

Scope and Principles
- Windows-first; WPF on .NET 8; EF Core + SQLite; DI + Logging via Microsoft.Extensions.*
- Architect → Implement → Validate: Design (acceptance criteria) before code, then tests, then implementation, then measure/verify.
- Assume Production: Even dev builds should be reproducible, diagnosable, and updatable.
- Security-by-default: No secrets in repo; least-privilege; signed artifacts before distribution.
- Documentation is living: Update phases-progress.md in the same PR as feature changes.

Branching and Change Management
- main: protected; always shippable; green CI required.
- feature/<short-name>: feature work; draft PR early; small, frequent merges.
- fix/<ticket#-summary>: bug fixes; prefer small PRs.
- hotfix/<version>: patching released artifacts.
- Pull Requests: include:
  - Summary, scope, and acceptance criteria checked off.
  - Evidence: test results, screenshots/logs for UI where applicable.
  - Docs updates: phases-progress.md “Recent Changes” + any runbooks affected.

Versioning and Releases
- Semantic Versioning: MAJOR.MINOR.PATCH (e.g., 0.3.1).
- Pre-releases: -alpha.N / -beta.N for staged rollouts.
- Release Notes: highlight features, fixes, known issues, upgrade notes.
- Installers: Today MSI (WiX) for dev/testing; MSIX planned for per-user installs.

Toolchain and Build
- .NET SDK pinned via global.json; use the same SDK in CI and locally.
- Standard commands:
  - dotnet restore
  - dotnet build -c Release
  - dotnet test -c Release -v minimal
- Artifacts:
  - MSI: src/Installer.Msi/bin/x64/Release/*.msi
  - Logs, diagnostics bundles exported on CI failure.

Quality Gates (PR/CI)
- All tests pass:
  - Unit, integration (SQLite in-memory), UI smoke and automation.
- Static checks (where configured): analyzers and style rules.
- Packaging step builds MSI in Release; MSIX to be added later.
- On failure: export diagnostics bundle; attach to CI artifacts.

Testing Strategy
- Unit: ViewModels, services, validators.
- Integration: Database (SQLite in-memory), file system interactions, CSV services.
- UI: Automation tests with resilient selectors (AutomationId first, Name fallback) and smoke coverage for core flows.
- Exit codes: Headless operations (CSV, diagnostics) return deterministic exit codes covered by tests.

Documentation Governance
- phases-progress.md: authoritative status and roadmap; update when features move states (❌ → ⚠️/🚧 → ✅).
- project-documentation/operator-runbook.md: operational and headless usage.
- tests/README.md: test conventions and troubleshooting.
- Add “Recent Changes” entries with date and concise summary in phases-progress.md for every PR.

Security and Compliance
- No secrets in code or repo; use environment variables or OS keychain/DPAPI.
- Code signing planned: sign binaries and installers prior to external distribution.
- Supply chain hygiene: pin dependencies where feasible; produce SBOM in CI when we enable signing pipeline.
- Privacy-by-design: telemetry opt-in only; respect OS privacy settings.

CI/CD Secrets and Repo Variables (GitHub Actions)
- Required for signing and publishing:
  - Secrets:
    - MSIX_PFX_B64: Base64-encoded .pfx content for MSIX signing (OV/EV cert recommended). Never commit the .pfx.
      - Create with PowerShell: [Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\path\to\code-signing.pfx'))
    - MSIX_PFX_PASSWORD: Password for the .pfx (use a strong unique passphrase).
    - WINGET_TOKEN: PAT with permissions to submit PRs to the winget-pkgs repo (scope: repo). Rotate regularly.
  - Variables (Repository → Settings → Secrets and variables → Actions → Variables):
    - WINGET_IDENTIFIER: The WinGet package identifier (e.g., InventoryApp.KitchenInventory)
- Governance:
  - Store under Repository → Settings → Secrets and variables → Actions.
  - Rotate secrets regularly; revoke and replace promptly on exposure.
  - Use least privilege scopes; restrict maintainers with access.
  - Do not echo secrets in logs. Masked by default in Actions, but also avoid printing.

Observability
- Structured logging; log rotation; avoid PII unless necessary.
- Diagnostics bundle export available from UI and headless; include logs, config, and anonymized data.
- Crash reporting: planned; add consent prompt and local crash dump capture.

Packaging and Distribution
- MSI via WiX: used for dev/testing installers; keep Heat/harvesting transforms updated as binaries change.
- MSIX (planned): per-user install, code signing hooks; update channel via MSIX incremental updates or in-app notifier.
- Distribution: winget manifest (planned) for internal/external distribution.

Accessibility and UX
- Use AutomationId on all actionable UI elements; Name fallback only for tests.
- Keyboard navigation, screen-reader friendly, proper focus visuals.
- DPI scaling and high-contrast support; verify at 100/150/200%.

Change Procedure (Feature Template)
1) Define: Update phases-progress.md with the feature under the appropriate phase; write Acceptance Criteria.
2) Tests First: Add/update unit/integration/UI tests reflecting acceptance criteria.
3) Implement: Code to satisfy tests; instrument logging; wire AutomationIds.
4) Validate: Run dotnet test locally; manual smoke where applicable.
5) Docs: Update phases-progress.md “Recent Changes”; update runbooks if CLI/headless behavior changed.
6) PR: Create PR with evidence and checklists; ensure CI is green.

Incident and Diagnostics Procedure
- On CI failure: collect and publish diagnostics bundle.
- On user-reported issue: request user to export diagnostics bundle from UI or headless, attach to ticket.

Roadmap Integration
- Any change that shifts phase boundaries or acceptance criteria must update phases-progress.md in the same PR.

Ownership and Stewardship
- Document owners: Tech lead maintains this file and phases-progress.md; all contributors are responsible for updating docs adjacent to their changes.