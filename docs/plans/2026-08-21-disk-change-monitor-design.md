# Disk Change Monitor Design

## Goal

Create a local Windows `.exe` that lets the user monitor one or more disks or folders, with the main use case being the `C:\` drive. Each manual scan creates a snapshot. The application compares snapshots to show where storage increased or decreased.

## Scope

- User-selectable disks and folders; no automatic scanning on launch.
- Keep the 10 most recent completed snapshots per monitored location.
- Compare the latest two snapshots by default, with the ability to choose any two retained snapshots.
- Show new, deleted, enlarged, reduced, moved/renamed, and unchanged files.
- Aggregate file changes by directory and allow drilling down to file details.
- Show scan mode, duration, processed file count, skipped item count, and warnings.
- Export comparison results to CSV.
- Store all data locally; do not read file contents or send telemetry.

## Architecture

- UI layer for monitored locations, manual scan actions, history, filters, and results.
- Scanner abstraction with a fast NTFS reader as the preferred provider for NTFS volumes and a recursive ordinary scanner as the fallback.
- Snapshot store backed by SQLite, with indexed file paths and snapshot metadata.
- Difference engine that compares two snapshots and classifies file changes.
- Presentation-time directory aggregation so directory totals cannot diverge from file details.

## Scan Flow

1. Load monitored locations and retained history on startup without scanning.
2. On manual start, detect the target file system and try fast NTFS reading for NTFS volumes.
3. If fast reading is unavailable, incomplete, or lacks required access, offer an elevated restart and then fall back to ordinary scanning when necessary.
4. Write the result to a temporary snapshot and validate it before committing it as a completed snapshot.
5. Generate a comparison with the previous snapshot when one exists.
6. Retain only the newest 10 completed snapshots after the new snapshot is safely stored.

Each file record contains its full path, size, last-write time, attributes, and (when available) a file identifier to assist move/rename detection.

## UI

- Monitored locations bar with add/remove controls and `C:\` as the convenient default.
- Scan status bar showing last scan time, mode, duration, file count, skipped count, and warnings.
- Overview showing total net change, new-file size, deleted-file size, growth, and reduction.
- Difference table sortable by change size, with filters for new, deleted, enlarged, and reduced files.
- Directory view with expandable rows and file view for direct sorting.
- History view showing the 10 retained scans and allowing any two to be compared.

## Reliability and Error Handling

- Save under `%LOCALAPPDATA%\DiskChangeMonitor\`.
- Commit snapshots atomically through temporary storage so interruption cannot replace a valid prior snapshot with partial data.
- Skip inaccessible or transiently unavailable files while recording warnings; never interpret an unscanned path as deleted.
- Keep history when a disk is disconnected and mark that target as not scanned.
- Preserve the previous snapshot if database writing fails; retain a recoverable backup on database corruption.

## Testing

Test small and large directories, a full `C:\` scan, protected and busy files, file creation/deletion/growth/shrinkage, moves and renames, cancellation, crashes, disconnected volumes, fast-reader fallback, history retention, arbitrary snapshot comparison, and CSV export.

## Future Work

The snapshot format and difference UI are intentionally independent of the scanner implementation so that NTFS/MFT performance improvements can be added without redesigning history or comparison behavior.
