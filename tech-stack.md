# School Kitchen Inventory Management — Technology Stack
Version: Unreleased (current codebase targeting .NET 8.0)

This document describes the technologies currently used in the repository, along with versions, purposes, and configuration details. Where relevant, it also notes planned components that are referenced by the project plan but not yet implemented in code.

---

## Frontend Technologies

- Current status: Windows Desktop (WPF) UI implemented targeting net8.0-windows with UseWPF enabled. Core window and MVVM (ItemsViewModel) are in place, with validation and CRUD bound to EF Core.
- Planned (from project plan):
  - Windows Desktop (WPF) on .NET 8
    - Version: Microsoft.WindowsDesktop.App 8.x (planned)
    - Purpose: Desktop UI for inventory management using MVVM.
    - Configuration (planned example):
      ```xml
      <Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">
        <PropertyGroup>
          <OutputType>WinExe</OutputType>
          <TargetFramework>net8.0-windows</TargetFramework>
          <UseWPF>true</UseWPF>
          <Nullable>enable</Nullable>
        </PropertyGroup>
      </Project>
      ```

---

## Backend Technologies

- .NET SDK
  - Version: 8.0.413 (local SDK installed in .dotnet folder)
  - Purpose: Build and run the solution locally and in CI.
  - Notes: The console application targets .NET 8 (net8.0).

- .NET Runtime/Target Framework
  - Version: net8.0
  - Purpose: Target framework for QAAgentRunner and tests.
  - Configuration (QAAgentRunner.csproj):
    ```xml
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
      </PropertyGroup>
    </Project>
    ```

- Language
  - C# version: C# 12 (via net8.0)
  - Purpose: Implementation language for the console app and tests.

- Application
  - Project: src/QAAgentRunner (console app)
  - Purpose: Processes architecture and product documents to generate a QA report (qa-output.md).
  - Typical usage:
    ```powershell
    # Example (explicit paths)
    c:\InventoryApp\.dotnet\dotnet.exe run --project .\src\QAAgentRunner\QAAgentRunner.csproj -- \
      --architecture "c:\\InventoryApp\\project-documentation\\architecture-output.md" \
      --product "c:\\InventoryApp\\project-documentation\\product-manager-output.md" \
      --features "c:\\InventoryApp\\Features.md" \
      --featuresv1 "c:\\InventoryApp\\Featuresv1.md" \
      --output "c:\\InventoryApp\\project-documentation\\qa-output.md"
    ```

---

## Database Systems

- Current status: Implemented — SQLite + Entity Framework Core 8.0.8 with migrations (applied at startup). Default connection string points to %LocalAppData%\InventoryApp\kitchen.db via App.xaml.cs; DbContext and factory are registered via Microsoft.Extensions.Hosting DI.
- Planned (from project plan):
  - SQLite + Entity Framework Core
    - Version: TBD (to be selected when implementation starts)
    - Purpose: Local-first persistence for Items, Categories, Users, and UsageHistory with migrations.
    - Configuration (planned approach):
      - EF Core DbContext configured for SQLite connection string.
      - Integration tests using ephemeral SQLite files or in-memory provider.

---

## DevOps Tools

- GitHub Actions (CI)
  - Current status: Implemented — GitHub Actions workflow (.github/workflows/dotnet.yml) setting up .NET SDK, restore, build (Release), and runs tests for QAAgentRunner.Tests.
  - Actions used and versions:
    - actions/checkout@v4
    - actions/setup-dotnet@v4 (dotnet-version: 8.0.x)
  - Runner: windows-latest
  - Purpose: Restore dependencies, build, and run tests on pushes and pull requests.
  - Workflow (excerpt):
    ```yaml
    name: .NET CI
    on:
      push:
        branches: [ "**" ]
      pull_request:
        branches: [ "**" ]
    jobs:
      build-test:
        runs-on: windows-latest
        steps:
          - uses: actions/checkout@v4
          - uses: actions/setup-dotnet@v4
            with:
              dotnet-version: |
                8.0.x
          - name: Restore
            run: |
              dotnet restore ./tests/QAAgentRunner.Tests/QAAgentRunner.Tests.csproj
              dotnet restore ./src/QAAgentRunner/QAAgentRunner.csproj
          - name: Build
            run: |
              dotnet build ./tests/QAAgentRunner.Tests/QAAgentRunner.Tests.csproj --configuration Release --no-restore
              dotnet build ./src/QAAgentRunner/QAAgentRunner.csproj --configuration Release --no-restore
          - name: Test
            run: |
              dotnet test ./tests/QAAgentRunner.Tests/QAAgentRunner.Tests.csproj --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
    ```

- NuGet Package Source
  - Source: https://api.nuget.org/v3/index.json
  - Purpose: Primary package feed for restore.
  - Configuration (NuGet.config):
    ```xml
    <?xml version="1.0" encoding="utf-8"?>
    <configuration>
      <packageSources>
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
      </packageSources>
    </configuration>
    ```

- Local SDK installation
  - Location: c:\InventoryApp\.dotnet
  - Purpose: Self-contained SDK for consistent local builds.
  - Example usage:
    ```powershell
    c:\InventoryApp\.dotnet\dotnet.exe --info
    c:\InventoryApp\.dotnet\dotnet.exe restore
    c:\InventoryApp\.dotnet\dotnet.exe build --configuration Release
    c:\InventoryApp\.dotnet\dotnet.exe test --configuration Release
    ```

---

## Testing Frameworks

- Microsoft.NET.Test.Sdk
  - Version: 17.10.0
  - Purpose: Test host/integration with test frameworks in .NET.

- xUnit
  - Version: 2.6.6
  - Purpose: Unit test framework for the QAAgentRunner tests.

- xunit.runner.visualstudio
  - Version: 2.5.7
  - Purpose: Enables Visual Studio and CLI discovery/execution and results reporting.

- FluentAssertions
  - Version: 6.12.0
  - Purpose: Expressive assertions for readable tests.

- Example test invocation
  ```powershell
  # Build and run tests with TRX output
  c:\InventoryApp\.dotnet\dotnet.exe test .\tests\QAAgentRunner.Tests\QAAgentRunner.Tests.csproj \
    --configuration Release \
    --logger "trx;LogFileName=test-results.trx"
  ```

---

## Version Matrix (Quick Reference)

- .NET SDK: 8.0.413 (local), 8.0.x (CI)
- Target Framework: net8.0
- GitHub Actions:
  - actions/checkout@v4
  - actions/setup-dotnet@v4
  - Runner: windows-latest
- NuGet feed: nuget.org (v3 API)
- Test packages:
  - Microsoft.NET.Test.Sdk: 17.10.0
  - xunit: 2.6.6
  - xunit.runner.visualstudio: 2.5.7
  - FluentAssertions: 6.12.0

---

## Notes and Next Steps

- When the frontend (WPF) and database (EF Core + SQLite) are introduced, update this document with the exact package versions and configuration snippets (DbContext, connection strings, app/runtime identifiers).
- Consider pinning SDK/Runtime explicitly in global.json to guarantee version lock across machines.
- Consider adding coverage tooling (e.g., coverlet) and publishing TRX and coverage reports from CI artifacts.