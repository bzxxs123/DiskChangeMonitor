# Disk Change Monitor Design

## Goal

Create a local Windows `.exe` that imports WizTree CSV exports for monitored disks or folders, mainly `C:\`, and compares historical exports to show logical-size and allocated-space changes.

## Scope

- User manually selects a WizTree CSV for each import; startup never scans or imports automatically.
- Retain the 5 most recent completed snapshots per monitored root.
- Compare only the latest two completed snapshots.
- Show new, deleted, enlarged, reduced, unchanged, and path-based moved/renamed items.
- Aggregate changes by directory and drill down to file rows.
- Support WizTree 4.28 columns: `文件名称`, `大小`, `分配`, `修改时间`, `属性`, `文件`, `文件夹`, and summary columns.
- Support CSV exports around 280 MB without loading the whole file or all rows into memory.
- Export comparison results to CSV. Store all data locally and never read file contents or send telemetry.
- Do not copy imported source CSVs into the application data directory; retain only source path, size, timestamp, and a content fingerprint.

## Architecture

- WPF UI for monitored roots, manual CSV import, history, filters, and results.
- WizTree CSV parser independent from WizTree binaries or licensing.
- SQLite snapshot store with indexed paths and transactional commits.
- Pure difference engine comparing both logical size (`大小`) and allocated space (`分配`).
- Directory aggregation computed from file changes at presentation time.

## Import Flow

1. Load roots and retained history without importing.
2. User chooses a WizTree CSV and monitored root.
3. Stream-parse UTF-8/BOM, quoted Chinese paths, scientific-notation numbers, localized dates, and blank folder fields.
4. Validate required columns, batch-insert rows into a temporary SQLite snapshot, and periodically report progress.
5. Commit only validated snapshots, compare with the previous snapshot, then prune history to 10.

Each item stores path, logical size, allocated size, modified time, attributes, item kind, and optional folder summary values. Move/rename detection is path-based in v1 because CSV exports do not guarantee stable file IDs.

## UI

- Root list and `Import CSV` button.
- Import status showing source CSV, timestamp, parsed rows, ignored rows, and warnings.
- Overview showing logical-size and allocated-space net changes.
- Sortable/filterable directory and file views for new, deleted, enlarged, and reduced items.
- History view with 5 retained imports; the newest two are the active comparison pair.

## Reliability and Testing

Malformed rows are skipped with row numbers and reasons. Missing required columns reject the import without changing history. Failed writes preserve the prior snapshot. Large imports use bounded memory, SQLite transactions, indexes created after bulk insert, and database-side diff queries. The app requires a 64-bit build and reports estimated database space before import when possible. Tests cover 280 MB-scale exports, Chinese/quoted paths, scientific notation, malformed rows, size/allocation changes, history pruning, latest-two comparison, and CSV export.

## Future Work

The snapshot and diff layers remain independent so a future direct scanner can be added without redesigning history or comparison behavior.
