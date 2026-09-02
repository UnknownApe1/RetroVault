# ROM Manager — Milestone 1

A Windows 10/11 desktop application that safely indexes ROM and game files without moving, renaming, deleting, extracting, or modifying them.

## What works

- Unlimited recursive scan locations, including local, removable, and UNC paths
- Startup reconciliation and manual scans with cancellation/progress
- Background-thread scanning with progress throttled to four UI updates per second
- Persistent interrupted-scan detection and automatic incremental recovery on restart
- Client-side scan timestamp comparison compatible with SQLite DateTimeOffset storage
- SQLite WAL mode and busy-timeout handling so the library remains usable while scanning
- Single-instance protection so two scans cannot write to the same database concurrently
- Parameterized missing-file finalization that avoids EF Core enum-translation failures on large reconciliation scans
- Incremental comparison using full path, size, and modified time
- Missing-file retention for disconnected drives and shares
- Data-driven platform/format catalog covering 61 console, handheld, computer, arcade, and fallback families
- No-Intro/TOSEC-style title, region, language, revision, disc, and track parsing
- CUE/BIN and track grouping plus multi-disc title grouping
- ZIP content inspection without extraction; 7z/RAR paths remain read-only candidates for later deep inspection
- 128 KiB sampled quick hash for the normal scan path
- SHA-256 only on quick-hash collisions or explicit verification
- Exact duplicate status only after matching full SHA-256 hashes
- SQLite library with an internal versioned schema migration
- Search by game title, physical filename, or path; filter by system
- High-contrast Midnight Teal interface with explicit readable text colors and independently scrollable system navigation
- Game/file details, open containing folder, copy path, and verify SHA-256
- Daily file logging under `%LOCALAPPDATA%\CozziForged\RomManager\Logs`
- xUnit coverage for parsing, grouping, hashing, enumeration, and ambiguous format hints
- Security-patched EF Core 10.0.11 dependency line (SQLitePCLRaw 2.1.12 or newer transitively)

ROM files are opened read-only with shared-read access. The only files the application writes are its own database and logs under `%LOCALAPPDATA%\CozziForged\RomManager`.

## Project tree

```text
RomManager/
├── RomManager.sln
├── Directory.Build.props
├── Directory.Packages.props
├── Build.ps1
├── RomManager.App/              WPF views, ViewModels, startup, dependency injection
├── RomManager.Core/             models and testable scan/parser/hash/grouping services
├── RomManager.Database/         SQLite context, repository, versioned migration
├── RomManager.Formats/          JSON catalog, identification, ZIP inspection
├── RomManager.Infrastructure/   physical filesystem and daily file logger
└── RomManager.Tests/            xUnit tests organized by subsystem
```

## Build and run

Requirements:

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

From PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Build.ps1
dotnet run --project .\RomManager.App\RomManager.App.csproj
```

Or open `RomManager.sln` in Visual Studio 2026 with the .NET desktop development workload.

To create a self-contained Windows package:

```powershell
.\Build.ps1 -Publish
```

The output is placed in `artifacts\publish\win-x64`.

## First use

1. Select **Add Folder** and choose a ROM root.
2. Select **Scan Now**. The scan remains read-only against everything under that root.
3. Search or select a system in the left panel.
4. Select a game to inspect all physical versions/files.
5. Use **Verify SHA-256** for an explicit full-file verification. Likely duplicates are automatically fully verified when quick hashes collide.

Configured locations are reconciled automatically at startup. Unchanged files avoid parsing and hashing. Files that disappear are marked `Missing`; reconnecting and rescanning restores them.

If the application or computer stops during a scan, the location retains a started-but-not-completed marker. The next launch reports that an interrupted scan was found and starts a recovery reconciliation. The recovery enumerates the location again for correctness, but files already committed with unchanged size and modified time skip parsing and hashing. Selecting **Cancel** or confirming an exit during a scan is safe for the same reason.

## Important implementation decisions

- **Quick hash is not proof of duplication.** It only selects candidates. The application marks `Duplicate` after full SHA-256 equality.
- **Uncertain filename similarity is not auto-merged.** Milestone 1 groups deterministic normalized titles within one system. A future review screen can offer fuzzy matches.
- **Ambiguous extensions use path hints and catalog priority.** The JSON catalog makes this replaceable by header-specific detectors without rewriting the scanner.
- **Database history is retained.** Missing records are not deleted during reconciliation.
- **Scanning favors correctness in Milestone 1.** The service boundaries allow a bounded parallel/batched pipeline in the next performance pass without coupling it to WPF.

## Milestone boundary

Metadata/cover downloads, emulator launching, destructive file organization, automatic fuzzy merges, and in-place archive extraction are intentionally excluded.
