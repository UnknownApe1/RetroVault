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
- One in-memory file snapshot per scan and batched unchanged-file updates, avoiding a database read/write cycle for every unchanged file
- Cached game and format lookups for substantially fewer database queries while indexing new files
- Missing-file retention for disconnected drives and shares
- Incomplete-enumeration protection: inaccessible paths are logged and missing-file finalization is skipped instead of creating false missing records
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
- Visible application version in both the title bar and header so installed builds are easy to identify
- Game/file details, open containing folder, copy path, and verify SHA-256
- Full UTF-8 CSV inventory export with source folder, physical path, parsed metadata, status, quick hash, and SHA-256
- Exact online-catalog verification against Libretro's maintained No-Intro and Redump DAT mirrors, cached locally for seven days
- SHA-1 plus CRC32 matching for regular files and ROM contents inside ZIP archives; filenames alone never produce a verified result
- Conservative preferred-copy recommendations that prioritize verified, available USA/World releases and avoid beta/prototype/demo/bad-dump/hack labels
- Rotating session logs under `%LOCALAPPDATA%\CozziForged\RomManager\Logs` (2 MiB per file, 20 files maximum, 14-day retention)
- Optional `--verbose-scan` diagnostics for per-file unchanged and unsupported skip reasons
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

## Applying later updates

Starting with v1.4.0, place the patch that matches your installed version in `C:\RomManager\Patches`, close ROM Manager, and run `Update.cmd`. The helper selects the newest patch, verifies the working tree, applies it, runs the release build, and records the successfully built version in local Git. It stops without applying anything when the patch does not match or the source has uncommitted changes.

## First use

1. Select **Add Folder** and choose a ROM root.
2. Select **Scan Now**. The scan remains read-only against everything under that root.
3. Search or select a system in the left panel.
4. Select a game to inspect all physical versions/files.
5. Use **Verify SHA-256** for an explicit full-file verification. Likely duplicates are automatically fully verified when quick hashes collide.
6. Use **Export CSV** to create a portable inventory of every indexed physical copy and its source folder.
7. Select a system and use **Verify Catalog** to download/cache its checksum DAT and verify each copy. Selecting **All systems** is supported but may take hours because complete hashes require reading every ROM.

Configured locations are reconciled when **Scan Now** is selected. Unchanged files avoid parsing and hashing. Files that disappear are marked `Missing`; reconnecting and rescanning restores them.

If the application or computer stops during a scan, the location retains a started-but-not-completed marker. The next launch reports that an interrupted scan was found and starts a recovery reconciliation. The recovery enumerates the location again for correctness, but files already committed with unchanged size and modified time skip parsing and hashing. Selecting **Cancel** or confirming an exit during a scan is safe for the same reason.

For detailed scanner diagnostics, launch `RomManager.exe --verbose-scan`. Normal logs record scan summaries, unsupported-extension totals, inaccessible paths, and errors. Verbose mode additionally records every unchanged or unsupported file. Routine Entity Framework SQL statements are filtered out. Logs rotate at 2 MiB and old files are automatically removed after 14 days or when 20 retained files already exist.

## Important implementation decisions

- **Quick hash is not proof of duplication.** It only selects candidates. The application marks `Duplicate` after full SHA-256 equality.
- **Catalog verification is exact.** `Verified` means the file bytes—or a ROM contained inside a ZIP—matched a published SHA-1 or size/CRC32 catalog record. `NoMatch` does not automatically mean bad; headered, transformed, encrypted, or compressed disc formats may not match the catalog's canonical representation.
- **Preferred is a recommendation, not a deletion decision.** It can be changed by later review tools, and v1.5 never moves, renames, or deletes ROMs.
- **Catalog provenance.** Automatic DAT downloads come from the CC BY-SA 4.0 [Libretro Database](https://github.com/libretro/libretro-database), which imports upstream No-Intro and Redump data and identifies source precedence in its repository documentation.
- **Uncertain filename similarity is not auto-merged.** Milestone 1 groups deterministic normalized titles within one system. A future review screen can offer fuzzy matches.
- **Ambiguous extensions use path hints and catalog priority.** The JSON catalog makes this replaceable by header-specific detectors without rewriting the scanner.
- **Database history is retained.** Missing records are not deleted during reconciliation.
- **Scanning favors correctness.** Unchanged database updates are batched, while HDD file reads remain ordered to avoid turning a sequential scan into random disk contention.

## Milestone boundary

Metadata/cover downloads, emulator launching, destructive file organization, automatic fuzzy merges, and in-place archive extraction are intentionally excluded.
