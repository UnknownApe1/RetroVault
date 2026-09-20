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
- ZIP, 7z, and RAR content inspection without extraction (via [SharpCompress](https://github.com/adamhathcock/sharpcompress))
- 128 KiB sampled quick hash for the normal scan path
- SHA-256 only on quick-hash collisions or explicit verification
- Exact duplicate status only after matching full SHA-256 hashes
- SQLite library with an internal versioned schema migration
- Search by game title, physical filename, or path; filter by system
- Paged library loading with a **Load more games** action for large libraries
- Detection confidence and reason tracking for ambiguous files, with a **Needs review** filter and manual system assignment
- Database backup/deferred restore and scan-location settings export/import
- Empty-library guidance for first use
- "Neon Cartridge" interface: a near-black ground with a teal-to-violet gradient identity (wordmark, primary action, version badge), evolved from the original Midnight Teal palette rather than replacing it. Toolbar actions are grouped into pill clusters (Cleanup, Verify, Export) that wrap onto a second row instead of clipping off the window edge as more tools were added
- Visible application version in both the title bar and header so installed builds are easy to identify
- Fully retemplated buttons, dropdowns, and checkboxes so disabled/checked/expanded states stay legible, instead of the default WPF chrome silently overriding the dark theme
- Game/file details, open containing folder, copy path, and verify SHA-256
- Full UTF-8 CSV inventory export with source folder, physical path, parsed metadata, status, quick hash, and SHA-256
- Exact online-catalog verification against Libretro's maintained No-Intro and Redump DAT mirrors, cached locally for seven days
- SHA-1 plus CRC32 matching for regular files and ROM contents inside ZIP/7z/RAR archives; filenames alone never produce a verified result
- Conservative preferred-copy recommendations that prioritize verified, available USA/World releases and avoid beta/prototype/demo/bad-dump/hack labels
- Likely-duplicate title review: a bucketed Levenshtein scan (with an explicit guard against numbered-sequel false positives) surfaces near-identical titles across the whole library for a human to merge or dismiss — nothing is merged automatically
- Multi-select bulk actions: exclude every non-preferred copy, or clear manual preference/exclusion overrides, across all selected games at once
- Best-effort box art from [libretro-thumbnails](https://github.com/libretro-thumbnails) in the detail panel for the selected game, matched against the verified catalog name (falling back to the parsed title plus region), cached locally with a negative-result cache so a miss is not retried for 30 days. Matching depends on the title being close to No-Intro naming — collections whose filenames carry their own ranking/numbering prefix will mostly miss. (A thumbnail was previously also shown per row in the dense game list; removed since a 30px image added little at that size and had to reload on every virtualized row recycle during scrolling, causing a visible pop-in flicker.)
- Export Good Roms: copies every game's preferred, non-excluded version into a destination folder organized one subfolder per system, preserving the original filename. Re-running it only copies files that are new or changed (matched by size), so it is safe to use repeatedly as your curation improves. Source files are only ever opened for reading.
- Wanted flag: mark individual games (via multi-select) as Wanted, filter the library down to just them, and use Export Wanted Games to copy only that curated subset — the same preferred-copy scoring and folder-per-system layout as Export Good Roms, just scoped to games you've explicitly chosen instead of the whole library.
- Clean Up Titles: scans the whole library for display-name suggestions — stripping a numeric catalog-rank prefix (e.g. "0002 Dragon Quest 1+2" → "Dragon Quest 1+2") or, for a game with a verified copy, adopting its verified catalog name — and presents them one at a time for approval or skip, plus an Apply All for the remaining list. Only the display name (`CanonicalTitle`/`SortTitle`) changes; the scan-matching key (`NormalizedTitle`) and the physical files are never touched.
- A REGION column in the game list, sourced from the already-stored preferred-copy region — no additional scanning or lookups required.
- SIZE and DUPLICATES columns in the game list: total on-disk size across a game's non-missing copies, and how many of those copies are byte-identical duplicates of each other.
- A game count next to each system in the sidebar (e.g. "Super Nintendo (1,204)"), counting distinct games rather than files — a game with several duplicate or regional copies still counts once, so the number isn't inflated by exact duplicates or multi-file titles.
- Merge Exact Duplicates: finds games that share the exact same title within a system (common after Clean Up Titles normalizes several variants down to one name) and merges them one group at a time or via Merge All. The copy with the most files is kept, every other copy's files are reassigned to it, and preferred-copy scoring is recalculated — physical files are never touched.
- Duplicate Report: a read-only CSV export of every byte-identical duplicate file (grouped by SHA-256), including how much disk space could be reclaimed by keeping one copy per group. Nothing is deleted; it's a report for you to act on outside the app if you choose.
- Incremental catalog verification: by default, Verify Catalog only hashes files that have never been checked (or previously errored), so repeated or interrupted runs are fast instead of re-hashing the whole library every time. A "Re-verify all" checkbox opts back into a full recheck.
- Folder-based PS3/RPCS3 game installs are now indexed. A folder carrying `PARAM.SFO` at its root or under `PS3_GAME/` (the standard marker for a decrypted PS3 install) is treated as one game, its real title read out of `PARAM.SFO`'s `TITLE` field instead of a product-code folder name, and its contents are never scanned as loose files. Export copies the whole folder tree; catalog verification and full SHA-256 (which apply to single-file dumps) are not attempted for these, since there's no per-file dump to check against a Redump/No-Intro catalog.
- Rotating session logs under `%LOCALAPPDATA%\CozziForged\RomManager\Logs` (2 MiB per file, 20 files maximum, 14-day retention)
- Optional `--verbose-scan` diagnostics for per-file unchanged and unsupported skip reasons
- xUnit coverage for parsing, grouping, hashing, enumeration, and ambiguous format hints
- Security-patched EF Core 10.0.11 dependency line (SQLitePCLRaw 2.1.12 or newer transitively)

Indexed ROM files are opened read-only with shared-read access and are never moved, renamed, deleted, or modified in place. Besides its own database, logs, and cache under `%LOCALAPPDATA%\CozziForged\RomManager`, the only files the application writes are the copies it makes into a destination folder you explicitly choose via **Export Good Roms** — a copy, never a move.

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
7. Select a system and use **Verify Catalog** to download/cache its checksum DAT and verify each copy. Selecting **All systems** is supported and reads every ROM the first time, but by default each run only checks files that haven't been checked yet (or previously errored) — safe to run repeatedly or resume later instead of committing to one long session. Check **Re-verify all** first if you specifically want to recheck files that already have a result.
8. Use **Review Duplicate Titles** to scan the whole library for likely-duplicate game titles (typos, alternate spellings, punctuation differences) and choose which copy to keep for each pair. Nothing merges until you pick a side.
9. Select multiple games in the list (click, Ctrl+click, Shift+click) and use **Exclude Non-Preferred Copies** to keep only the automatically preferred copy per game, or **Clear Overrides** to reset manual choices back to automatic.
10. Use **Export Good Roms** to copy every game's preferred copy into a folder you choose, organized one subfolder per system — a curated backup you can restore from if a device is reset or replaced. Use **Use This Copy** on any file first if you want a specific version exported instead of the automatic pick.
11. To export only a subset instead of the whole library, select games and use **Mark Wanted**, review them with the **Wanted** filter, then use **Export Wanted Games**. **Unmark Wanted** removes games from that set.
12. Use **Clean Up Titles** to review suggested display-name fixes (numeric rank prefixes, verified catalog names) and approve or skip each one. This only changes the name shown in the app — files are never renamed.
13. After cleaning up titles, use **Merge Exact Duplicates** to combine games that now share the exact same title within a system (a common side effect of step 12, since different romsets often named the same game differently before cleanup). Merging keeps the copy with the most files and reassigns the rest onto it.
14. Use **Duplicate Report** at any time to export a CSV of byte-identical duplicate files and how much space they're taking up — a read-only report, not a deletion tool.
15. Use **Load more games** for libraries larger than the first page; changing the search or filter starts again at the first page.
16. Use **Needs review** to find low-confidence detections. Select the intended system in the sidebar and choose **Assign to Selected System** in the file details; this changes only the index.
17. Use **Backup Database**, **Restore Database**, **Export Settings**, and **Import Settings** from the toolbar for recovery and portability. A database restore is applied safely on the next application start.

Configured locations are reconciled when **Scan Now** is selected. Unchanged files avoid parsing and hashing. Files that disappear are marked `Missing`; reconnecting and rescanning restores them.

If the application or computer stops during a scan, the location retains a started-but-not-completed marker. The next launch reports that an interrupted scan was found and starts a recovery reconciliation. The recovery enumerates the location again for correctness, but files already committed with unchanged size and modified time skip parsing and hashing. Selecting **Cancel** or confirming an exit during a scan is safe for the same reason.

For detailed scanner diagnostics, launch `RomManager.exe --verbose-scan`. Normal logs record scan summaries, unsupported-extension totals, inaccessible paths, and errors. Verbose mode additionally records every unchanged or unsupported file. Routine Entity Framework SQL statements are filtered out. Logs rotate at 2 MiB and old files are automatically removed after 14 days or when 20 retained files already exist.

## Important implementation decisions

- **Quick hash is not proof of duplication.** It only selects candidates. The application marks `Duplicate` after full SHA-256 equality.
- **Catalog verification is exact.** `Verified` means the file bytes—or a ROM contained inside a ZIP—matched a published SHA-1 or size/CRC32 catalog record. `NoMatch` does not automatically mean bad; headered, transformed, encrypted, or compressed disc formats may not match the catalog's canonical representation.
- **Preferred is a recommendation, not a deletion decision.** It can be changed by later review tools, and ROM Manager never moves, renames, or deletes ROMs — Export Good Roms only ever copies.
- **CUE/BIN export completeness depends on the format catalog.** A `.cue` is only exported correctly if its `.bin` track file was itself scanned as a candidate format for that system. Every CUE-based system in the catalog (PSX, Saturn, Sega CD, 3DO, Dreamcast, Jaguar CD, PC Engine CD, Neo Geo CD, CD-i, Amiga CD32, FM Towns) now lists `.bin` as a `DiscImage` candidate, resolved against the sibling `.cue`/`.chd` entries by folder-name and path hints when more than one system claims the extension.
- **Catalog provenance.** Automatic DAT downloads come from the CC BY-SA 4.0 [Libretro Database](https://github.com/libretro/libretro-database), which imports upstream No-Intro and Redump data and identifies source precedence in its repository documentation.
- **Uncertain filename similarity is not auto-merged.** Scanning groups deterministic normalized titles within one system; the **Review Duplicate Titles** screen surfaces likely fuzzy matches for a human to merge or dismiss, one pair at a time.
- **Title cleanup only ever changes the display name.** `NormalizedTitle` — the sole key used to match a scanned file back to its `Game` row — is never touched by **Clean Up Titles**, so applying or skipping a suggestion carries no risk of creating a duplicate `Game` row or breaking a future rescan.
- **Exact-title merges pick a "keep" copy by file count, not quality.** Two `Game` rows with the identical title in the same system are merged by keeping whichever already has the most files (ties broken by lowest Id) and reassigning the rest onto it; which physical file actually gets exported is still decided separately by the normal preferred-copy scoring afterward, not by which row survived the merge.
- **Catalog verification is incremental by default.** A file whose `CatalogStatus` is `Verified`, `NoMatch`, or `Unsupported` is a stable outcome that only changes if the catalog itself changes, so a default **Verify Catalog** run skips those and only hashes files that are `Unknown` or previously `Error`ed. This is what makes repeated or resumed runs cheap instead of re-hashing the whole library from scratch every time; **Re-verify all** opts back into a full recheck.
- **Ambiguous extensions use path hints and catalog priority.** The JSON catalog makes this replaceable by header-specific detectors without rewriting the scanner. A folder hint matches the system key as a whole token (e.g. a `PS3_Games` folder hints `PS3`, not just an exact `/PS3/` path segment) so it isn't limited to folders named exactly after the system; without any hint, resolution falls back to raw format priority, which can pick the wrong system when several share an extension (fixed for `.iso` after a real case where `PS3_Games`-folder discs with no matching hint were being cataloged as PS2 because PS2's priority was higher).
- **Ambiguous detections are reviewable.** Files below 75% detection confidence appear under **Needs review** with their confidence and reason. Manual assignment updates only the indexed system/format metadata; physical files are never moved or renamed.
- **Database restore is staged safely.** The selected backup is copied to a pending file, the application closes, and startup replaces the active database before SQLite is opened. Create a backup before restoring.
- **Folder-based installs: PS3/RPCS3 only, detected by a `PARAM.SFO` marker.** A folder carrying `PARAM.SFO` at its root or under `PS3_GAME/` is indexed as one game; its contents are never recursed into as separate loose files. Other systems distributed as a folder tree (rather than a single file) are still invisible to the scanner — this is a PS3-specific detector, not a general directory-game framework, since `PARAM.SFO` is a PS3/PSP/Vita-specific marker. A folder's "quick hash" is a hash of its `PARAM.SFO` only (hashing a many-GB directory tree isn't practical during a routine scan), so exact-duplicate detection across directory installs is not attempted, and neither is catalog verification or full SHA-256 (both apply to single-file dumps, which a decrypted install isn't). Export copies the whole directory tree via `Directory.Copy`-style recursion, judged "already exported" by total size rather than a per-file walk.
- **Database history is retained.** Missing records are not deleted during reconciliation.
- **Scanning favors correctness.** Unchanged database updates are batched, while HDD file reads remain ordered to avoid turning a sequential scan into random disk contention.

## Milestone boundary

Emulator launching, automatic fuzzy merges, and in-place archive extraction are intentionally excluded. Cover art download (read-only, best-effort, cached locally) is now included; broader metadata download (descriptions, release dates, genres) is not. Organizing files is included as a non-destructive copy (Export Good Roms) — the source library is never reorganized, moved, or altered in place.
