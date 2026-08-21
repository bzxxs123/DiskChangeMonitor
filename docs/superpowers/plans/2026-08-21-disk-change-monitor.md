# Disk Change Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local Windows `.exe` that manually imports WizTree CSV exports, retains the latest 10 snapshots, and shows logical-size and allocated-space changes.

**Architecture:** .NET 8 WPF separates the UI, WizTree CSV parser, SQLite snapshot store, and pure diff engine. WizTree remains an external user-managed scanner; the app never bundles or modifies it.

**Tech Stack:** C#/.NET 8, WPF, `Microsoft.Data.Sqlite`, xUnit, self-contained win-x64 publish.

**Spec:** `docs/plans/2026-08-21-disk-change-monitor-design.md`

## Global Constraints

- Manual CSV import only; no automatic scanning on launch.
- Keep 5 newest completed snapshots per monitored root.
- Required CSV columns are `文件名称`, `大小`, `分配`, `修改时间`, and `属性`.
- CSV imports around 280 MB must use bounded memory and streaming/batched database writes.
- Reject missing required columns without changing history; skip malformed rows with warnings.
- Never read file contents or transmit metadata.
- Use a 64-bit self-contained build; never duplicate the source CSV into application storage.

---

### Task 1: Scaffold Solution and Domain Models

**Files:** `src/DiskChangeMonitor/DiskChangeMonitor.csproj`, `src/DiskChangeMonitor/App.xaml`, `src/DiskChangeMonitor/App.xaml.cs`, `src/DiskChangeMonitor/Models/*.cs`, `tests/DiskChangeMonitor.Tests/DiskChangeMonitor.Tests.csproj`.

- [ ] Create the WPF and xUnit projects targeting `net8.0-windows`, domain records for `MonitoredLocation`, `FileEntry`, `SnapshotMetadata`, `SnapshotData`, and `ImportResult`.
- [ ] Add model tests for required fields and item-kind inference inputs.
- [ ] Run `dotnet test` and commit `chore: scaffold disk monitor solution`.

### Task 2: Implement WizTree CSV Parser

**Files:** `src/DiskChangeMonitor/Import/WizTreeCsvParser.cs`, `src/DiskChangeMonitor/Import/CsvFieldParser.cs`, `tests/DiskChangeMonitor.Tests/Import/WizTreeCsvParserTests.cs`.

- [ ] Write failing tests for UTF-8/BOM, CRLF, quoted Chinese paths, scientific notation, blank folder fields, required-column validation, malformed rows, localized dates, and a generated large-file streaming fixture.
- [ ] Implement RFC-compatible streaming field parsing, invariant numeric parsing, date parsing, row-level warnings, item-kind inference, and bounded buffering.
- [ ] Ensure missing required columns fail before any snapshot can be committed.
- [ ] Run parser tests and commit `feat: import WizTree CSV snapshots`.

### Task 3: Implement Transactional SQLite Snapshot Store

**Files:** `src/DiskChangeMonitor/Storage/ISnapshotStore.cs`, `src/DiskChangeMonitor/Storage/SqliteSnapshotStore.cs`, `src/DiskChangeMonitor/Storage/DatabaseInitializer.cs`, `tests/DiskChangeMonitor.Tests/Storage/SqliteSnapshotStoreTests.cs`.

- [ ] Add `Microsoft.Data.Sqlite`; define `InitializeAsync`, `SaveAsync`, `ListAsync`, `LoadAsync`, and `PruneAsync` interfaces.
- [ ] Test round trips, newest-first history, failed transaction preservation, pruning to 5, and batched inserts.
- [ ] Store under `%LOCALAPPDATA%\\DiskChangeMonitor\\snapshots.db` with parameterized commands, 1,000-row transactions, temporary staging, and atomic completed status.
- [ ] Run storage tests and commit `feat: add transactional snapshot storage`.

### Task 4: Implement Diff Engine and Directory Aggregation

**Files:** `src/DiskChangeMonitor/Diff/*.cs`, `tests/DiskChangeMonitor.Tests/Diff/DiffEngineTests.cs`.

- [ ] Test new, deleted, enlarged, reduced, unchanged, path-based move/rename, logical-size deltas, allocated-space deltas, latest-two selection, and deterministic sorting.
- [ ] Implement `DiffEngine.Compare(SnapshotData older, SnapshotData newer) : DiffReport` with file changes, directory aggregates, totals, and both metrics.
- [ ] Run diff tests and commit `feat: add snapshot difference engine`.

### Task 5: Wire Import Workflow

**Files:** `src/DiskChangeMonitor/Import/ImportCoordinator.cs`, `tests/DiskChangeMonitor.Tests/Import/ImportCoordinatorTests.cs`.

- [ ] Test successful import, warning propagation, source CSV fingerprint, missing columns, malformed rows, latest-two selection, and unchanged history on failure.
- [ ] Implement streaming parse, validate, temporary staging, batched SQLite writes, post-import indexing, comparison with the immediately previous snapshot, and prune-after-commit.
- [ ] Keep only source path, file size, modified timestamp, and a content fingerprint; leave the 280 MB source file in place.
- [ ] Run coordinator tests and commit `feat: wire CSV import workflow`.

### Task 6: Build WPF UI

**Files:** `src/DiskChangeMonitor/ViewModels/*.cs`, `src/DiskChangeMonitor/Views/*.xaml`, `src/DiskChangeMonitor/Views/*.xaml.cs`, `tests/DiskChangeMonitor.Tests/ViewModels/MainViewModelTests.cs`.

- [ ] Test manual-only import, progress, warnings, cancellation, latest-two comparison, and history list display.
- [ ] Implement async commands, CSV file picker, root list, import status, overview totals, filters, sortable directory/file grids, and a history list showing the retained 5 imports.
- [ ] Run tests and `dotnet build`, then commit `feat: add disk monitor interface`.

### Task 7: Add CSV Export and Package the EXE

**Files:** `src/DiskChangeMonitor/Export/CsvExporter.cs`, `src/DiskChangeMonitor/App.xaml.cs`, `src/DiskChangeMonitor/Properties/PublishProfiles/win-x64.pubxml`, `tests/DiskChangeMonitor.Tests/Export/CsvExporterTests.cs`.

- [ ] Test quoting commas, quotes, newlines, UTF-8 paths, and stable columns.
- [ ] Implement UTF-8 CSV export and clear ignored-row warnings.
- [ ] Add a self-contained win-x64 publish profile and verify the generated `.exe` on Windows.
- [ ] Run all tests, publish Release, and commit `feat: package disk monitor for Windows`.

### Task 8: End-to-End Verification and Documentation

**Files:** `tests/DiskChangeMonitor.Tests/Integration/SnapshotComparisonTests.cs`, `README.md`, `docs/test-plan.md`.

- [ ] Test two imported exports saved, reloaded, and compared with expected deltas, including a large generated export with bounded memory.
- [ ] Run `dotnet test -c Release` and manually import a full `C:\` WizTree export, malformed rows, history pruning, and CSV export.
- [ ] Document export scope limitations and path-based move detection.
- [ ] Commit `test: verify end-to-end disk monitoring workflow`.
