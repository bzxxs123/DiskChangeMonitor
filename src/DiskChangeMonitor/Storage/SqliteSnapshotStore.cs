using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskChangeMonitor.Models;
using Microsoft.Data.Sqlite;

namespace DiskChangeMonitor.Storage
{
    public sealed class SqliteSnapshotStore : ISnapshotStore
    {
        private const int InsertBatchSize = 1000;
        private const int KeepSnapshots = 5;

        private readonly string _databasePath;
        private readonly string _connectionString;

        public SqliteSnapshotStore(string? databasePath = null)
        {
            _databasePath = databasePath ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DiskChangeMonitor",
                    "snapshots.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString();
        }

        public string DatabasePath => _databasePath;

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS snapshots (
                    id TEXT PRIMARY KEY,
                    root_path TEXT NOT NULL,
                    imported_at TEXT NOT NULL,
                    source_path TEXT NOT NULL,
                    source_bytes INTEGER NOT NULL,
                    fingerprint TEXT NOT NULL,
                    rows INTEGER NOT NULL DEFAULT 0,
                    ignored_rows INTEGER NOT NULL DEFAULT 0,
                    status TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS entries (
                    snapshot_id TEXT NOT NULL,
                    path TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    allocated INTEGER NOT NULL,
                    modified_ticks INTEGER NULL,
                    attributes TEXT NULL,
                    is_directory INTEGER NOT NULL,
                    file_count INTEGER NULL,
                    folder_count INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS idx_entries_snapshot_path
                    ON entries(snapshot_id, path);
                """;
            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<SnapshotMetadata> BeginImportAsync(
            MonitoredLocation root,
            string sourcePath,
            long sourceBytes,
            string fingerprint,
            DateTime? importedAt = null,
            CancellationToken ct = default)
        {
            var id = "snap-" + Guid.NewGuid().ToString("N");
            var imported = importedAt ?? DateTime.Now;

            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO snapshots (id, root_path, imported_at, source_path, source_bytes, fingerprint, rows, ignored_rows, status)
                VALUES (@id, @root, @importedAt, @sourcePath, @sourceBytes, @fingerprint, 0, 0, @status)
                """;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@root", root.RootPath);
            command.Parameters.AddWithValue("@importedAt", FormatDateTime(imported));
            command.Parameters.AddWithValue("@sourcePath", sourcePath);
            command.Parameters.AddWithValue("@sourceBytes", sourceBytes);
            command.Parameters.AddWithValue("@fingerprint", fingerprint);
            command.Parameters.AddWithValue("@status", SnapshotMetadata.Staging);
            await command.ExecuteNonQueryAsync(ct);

            return new SnapshotMetadata(id, root.RootPath, imported, sourcePath, sourceBytes, fingerprint, 0, 0, SnapshotMetadata.Staging);
        }

        public async Task AppendRowsAsync(string snapshotId, IEnumerable<FileEntry> rows, CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionAsync(ct);
            var batch = new List<FileEntry>(InsertBatchSize);
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                batch.Add(row);
                if (batch.Count >= InsertBatchSize)
                {
                    await InsertBatchAsync(connection, snapshotId, batch, ct);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await InsertBatchAsync(connection, snapshotId, batch, ct);
            }
        }

        public async Task<SnapshotMetadata> CommitAsync(string snapshotId, long parsedRows, long ignoredRows, CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE snapshots
                    SET rows = @rows, ignored_rows = @ignored, status = @status
                    WHERE id = @id
                    """;
                update.Parameters.AddWithValue("@rows", parsedRows);
                update.Parameters.AddWithValue("@ignored", ignoredRows);
                update.Parameters.AddWithValue("@status", SnapshotMetadata.Completed);
                update.Parameters.AddWithValue("@id", snapshotId);
                await update.ExecuteNonQueryAsync(ct);
            }

            await using (var index = connection.CreateCommand())
            {
                index.Transaction = transaction;
                index.CommandText =
                    "CREATE INDEX IF NOT EXISTS idx_entries_snapshot_path ON entries(snapshot_id, path)";
                await index.ExecuteNonQueryAsync(ct);
            }

            var rootPath = await GetRootPathAsync(connection, transaction, snapshotId, ct);
            await PruneAsync(connection, transaction, rootPath, ct);

            await transaction.CommitAsync(ct);
            return await GetMetadataAsync(snapshotId, ct) ?? throw new InvalidOperationException("提交后找不到快照。");
        }

        public async Task UpdateFingerprintAsync(string snapshotId, string fingerprint, CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE snapshots SET fingerprint = @fingerprint WHERE id = @id";
            command.Parameters.AddWithValue("@fingerprint", fingerprint);
            command.Parameters.AddWithValue("@id", snapshotId);
            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task CancelAsync(string snapshotId, CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

            await using (var entries = connection.CreateCommand())
            {
                entries.Transaction = transaction;
                entries.CommandText = "DELETE FROM entries WHERE snapshot_id = @id";
                entries.Parameters.AddWithValue("@id", snapshotId);
                await entries.ExecuteNonQueryAsync(ct);
            }

            await using (var snapshots = connection.CreateCommand())
            {
                snapshots.Transaction = transaction;
                snapshots.CommandText = "DELETE FROM snapshots WHERE id = @id";
                snapshots.Parameters.AddWithValue("@id", snapshotId);
                await snapshots.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        public async Task<IReadOnlyList<SnapshotMetadata>> ListAsync(string rootPath, CancellationToken ct = default)
        {
            var result = new List<SnapshotMetadata>();
            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, root_path, imported_at, source_path, source_bytes, fingerprint, rows, ignored_rows, status
                FROM snapshots
                WHERE root_path = @root AND status = @status
                ORDER BY imported_at DESC, id DESC
                """;
            command.Parameters.AddWithValue("@root", rootPath);
            command.Parameters.AddWithValue("@status", SnapshotMetadata.Completed);

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(ReadMetadata(reader));
            }

            return result;
        }

        public async Task<SnapshotData> LoadAsync(string snapshotId, CancellationToken ct = default)
        {
            var metadata = await GetMetadataAsync(snapshotId, ct);
            if (metadata == null)
            {
                throw new InvalidOperationException($"快照不存在: {snapshotId}");
            }

            return new SnapshotData(snapshotId, metadata.MonitoredRoot, () => QueryRows(snapshotId));
        }

        private async Task InsertBatchAsync(SqliteConnection connection, string snapshotId, List<FileEntry> rows, CancellationToken ct)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO entries
                    (snapshot_id, path, size, allocated, modified_ticks, attributes, is_directory, file_count, folder_count)
                VALUES
                    (@snapshotId, @path, @size, @allocated, @modifiedTicks, @attributes, @isDirectory, @fileCount, @folderCount)
                """;

            var snapshotIdParameter = command.Parameters.Add("@snapshotId", SqliteType.Text);
            var pathParameter = command.Parameters.Add("@path", SqliteType.Text);
            var sizeParameter = command.Parameters.Add("@size", SqliteType.Integer);
            var allocatedParameter = command.Parameters.Add("@allocated", SqliteType.Integer);
            var modifiedParameter = command.Parameters.Add("@modifiedTicks", SqliteType.Integer);
            var attributesParameter = command.Parameters.Add("@attributes", SqliteType.Text);
            var isDirectoryParameter = command.Parameters.Add("@isDirectory", SqliteType.Integer);
            var fileCountParameter = command.Parameters.Add("@fileCount", SqliteType.Integer);
            var folderCountParameter = command.Parameters.Add("@folderCount", SqliteType.Integer);

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                snapshotIdParameter.Value = snapshotId;
                pathParameter.Value = row.Path;
                sizeParameter.Value = row.Size;
                allocatedParameter.Value = row.Allocated;
                modifiedParameter.Value = row.Modified?.Ticks ?? (object)DBNull.Value;
                attributesParameter.Value = row.Attributes ?? string.Empty;
                isDirectoryParameter.Value = row.IsDirectory ? 1L : 0L;
                fileCountParameter.Value = row.FileCount ?? (object)DBNull.Value;
                folderCountParameter.Value = row.FolderCount ?? (object)DBNull.Value;
                await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        private async Task PruneAsync(SqliteConnection connection, SqliteTransaction transaction, string rootPath, CancellationToken ct)
        {
            var staleIds = new List<string>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText =
                    """
                    SELECT id FROM snapshots
                    WHERE root_path = @root AND status = @status
                    ORDER BY imported_at DESC, id DESC
                    LIMIT -1 OFFSET @keep
                    """;
                select.Parameters.AddWithValue("@root", rootPath);
                select.Parameters.AddWithValue("@status", SnapshotMetadata.Completed);
                select.Parameters.AddWithValue("@keep", KeepSnapshots);

                await using var reader = await select.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    staleIds.Add(reader.GetString(0));
                }
            }

            foreach (var staleId in staleIds)
            {
                await using (var entries = connection.CreateCommand())
                {
                    entries.Transaction = transaction;
                    entries.CommandText = "DELETE FROM entries WHERE snapshot_id = @id";
                    entries.Parameters.AddWithValue("@id", staleId);
                    await entries.ExecuteNonQueryAsync(ct);
                }

                await using (var snapshots = connection.CreateCommand())
                {
                    snapshots.Transaction = transaction;
                    snapshots.CommandText = "DELETE FROM snapshots WHERE id = @id";
                    snapshots.Parameters.AddWithValue("@id", staleId);
                    await snapshots.ExecuteNonQueryAsync(ct);
                }
            }
        }

        private async Task<string> GetRootPathAsync(SqliteConnection connection, SqliteTransaction transaction, string snapshotId, CancellationToken ct)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT root_path FROM snapshots WHERE id = @id";
            command.Parameters.AddWithValue("@id", snapshotId);
            var result = await command.ExecuteScalarAsync(ct);
            return result as string ?? throw new InvalidOperationException($"快照不存在: {snapshotId}");
        }

        private async Task<SnapshotMetadata?> GetMetadataAsync(string snapshotId, CancellationToken ct)
        {
            await using var connection = await OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, root_path, imported_at, source_path, source_bytes, fingerprint, rows, ignored_rows, status
                FROM snapshots WHERE id = @id
                """;
            command.Parameters.AddWithValue("@id", snapshotId);

            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadMetadata(reader) : null;
        }

        private static SnapshotMetadata ReadMetadata(DbDataReader reader)
        {
            return new SnapshotMetadata(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.ParseExact(reader.GetString(2), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetString(8));
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToString("O", CultureInfo.InvariantCulture);
        }

        private IEnumerable<FileEntry> QueryRows(string snapshotId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT path, size, allocated, modified_ticks, attributes, is_directory, file_count, folder_count
                FROM entries
                WHERE snapshot_id = @id
                ORDER BY path COLLATE BINARY
                """;
            command.Parameters.AddWithValue("@id", snapshotId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                yield return new FileEntry(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : new DateTime(reader.GetInt64(3)),
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.GetInt64(5) != 0,
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7));
            }
        }

        private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
    }
}
