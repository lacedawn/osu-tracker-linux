using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public class SqliteDatabaseManager : IDatabaseManager
    {
        private static readonly ILogger<SqliteDatabaseManager> _log = AppLogger.For<SqliteDatabaseManager>();

        private readonly string _connectionString;
        private readonly bool _isMemory;
        private SqliteConnection? _keepAliveMemoryConnection;
        private readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        public string DatabasePath { get; }
        public bool IsHealthy { get; private set; } = false;

        public SqliteDatabaseManager(string? customPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath) && (customPath == ":memory:" || customPath.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase)))
            {
                _isMemory = true;
                DatabasePath = customPath;
                _connectionString = customPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
                    ? customPath
                    : $"Data Source={customPath}";
            }
            else
            {
                _isMemory = false;
                DatabasePath = ResolveDatabasePath(customPath);
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = DatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };
                _connectionString = builder.ConnectionString;
            }
        }

        private static string ResolveDatabasePath(string? customPath)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(customPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    return customPath;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Custom database path invalid, falling back to default");
                }
            }

            try
            {
                Services.AppPaths.MigrateLegacyFiles();
                Services.AppPaths.EnsureDirectories();
                return Services.AppPaths.DatabasePath;
            }
            catch
            {
                string appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "circle-tracker");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                return Path.Combine(appData, "circle_tracker.db");
            }
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_isInitialized) return;

            await _initLock.WaitAsync(ct);
            try
            {
                if (_isInitialized) return;

                if (_isMemory && _keepAliveMemoryConnection == null)
                {
                    _keepAliveMemoryConnection = new SqliteConnection(_connectionString);
                    await _keepAliveMemoryConnection.OpenAsync(ct);
                    ApplyPragmas(_keepAliveMemoryConnection);
                }

                await EnsureIntegrityAsync(ct);
                await ApplyMigrationsAsync(ct);

                IsHealthy = true;
                _isInitialized = true;
                _log.LogInformation("Database initialized successfully at {Path}", DatabasePath);
            }
            catch (Exception ex)
            {
                IsHealthy = false;
                _log.LogError(ex, "Database initialization failed for {Path}", DatabasePath);
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private void ApplyPragmas(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                PRAGMA temp_store = MEMORY;";
            cmd.ExecuteNonQuery();
        }

        private async Task EnsureIntegrityAsync(CancellationToken ct)
        {
            if (_isMemory) return;

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync(ct);
                ApplyPragmas(connection);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = (string?)await cmd.ExecuteScalarAsync(ct);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogWarning("Database integrity check failed: {Result}. Attempting recovery/rotation", result);
                    await connection.CloseAsync();
                    RotateCorruptDatabase();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error performing integrity check");
            }
        }

        private void RotateCorruptDatabase()
        {
            if (_isMemory || !File.Exists(DatabasePath)) return;

            try
            {
                string corruptPath = Path.Combine(
                    Path.GetDirectoryName(DatabasePath) ?? "",
                    $"circle_tracker_corrupt_{DateTime.UtcNow:yyyyMMddHHmmss}.db");
                File.Move(DatabasePath, corruptPath);
                _log.LogWarning("Moved corrupt database from {OldPath} to {CorruptPath}", DatabasePath, corruptPath);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to rotate corrupt database");
            }
        }

        private async Task ApplyMigrationsAsync(CancellationToken ct)
        {
            await using var connection = await CreateConnectionAsync(ct);

            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL,
                    description TEXT NOT NULL
                );");

            int currentVersion = await connection.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");

            if (currentVersion < 1)
            {
                await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
                try
                {
                    await connection.ExecuteAsync(@"
                        CREATE TABLE IF NOT EXISTS sessions (
                            id TEXT PRIMARY KEY,
                            start_time TEXT NOT NULL,
                            end_time TEXT,
                            total_plays INTEGER NOT NULL DEFAULT 0,
                            playing_seconds INTEGER NOT NULL DEFAULT 0,
                            idle_seconds INTEGER NOT NULL DEFAULT 0,
                            efficiency_percent REAL NOT NULL DEFAULT 0.0,
                            client_version TEXT
                        );

                        CREATE TABLE IF NOT EXISTS plays (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            session_id TEXT,
                            timestamp TEXT NOT NULL,
                            beatmap_id INTEGER NOT NULL,
                            beatmap_set_id INTEGER NOT NULL,
                            beatmap_checksum TEXT,
                            beatmap_string TEXT NOT NULL,
                            beatmap_title TEXT NOT NULL,
                            beatmap_artist TEXT NOT NULL,
                            beatmap_version TEXT NOT NULL,
                            mods_bitfield INTEGER NOT NULL,
                            mods_string TEXT NOT NULL,
                            bpm INTEGER NOT NULL,
                            stars REAL NOT NULL,
                            aim REAL NOT NULL,
                            speed REAL NOT NULL,
                            cs REAL NOT NULL,
                            ar REAL NOT NULL,
                            od REAL NOT NULL,
                            hp REAL NOT NULL,
                            total_hits INTEGER NOT NULL,
                            hit_300 INTEGER NOT NULL,
                            hit_100 INTEGER NOT NULL,
                            hit_50 INTEGER NOT NULL,
                            hit_miss INTEGER NOT NULL,
                            accuracy REAL NOT NULL,
                            accuracy_reliable INTEGER NOT NULL,
                            is_complete INTEGER NOT NULL,
                            play_time_seconds INTEGER NOT NULL,
                            consecutive_play_count INTEGER NOT NULL,
                            game_mode INTEGER NOT NULL DEFAULT 0,
                            is_replay INTEGER NOT NULL DEFAULT 0,
                            detected_client TEXT NOT NULL,
                            FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE SET NULL
                        );

                        CREATE INDEX IF NOT EXISTS idx_plays_timestamp ON plays(timestamp DESC);
                        CREATE INDEX IF NOT EXISTS idx_plays_beatmap_id ON plays(beatmap_id);
                        CREATE INDEX IF NOT EXISTS idx_plays_checksum ON plays(beatmap_checksum);
                        CREATE INDEX IF NOT EXISTS idx_plays_stars ON plays(stars);
                        CREATE INDEX IF NOT EXISTS idx_plays_mods ON plays(mods_bitfield);
                        CREATE INDEX IF NOT EXISTS idx_plays_complete ON plays(is_complete);
                        CREATE INDEX IF NOT EXISTS idx_plays_session ON plays(session_id);",
                        transaction: tx);

                    await connection.ExecuteAsync(
                        "INSERT INTO schema_migrations (version, applied_at, description) VALUES (@version, @appliedAt, @description);",
                        new { version = 1, appliedAt = DateTime.UtcNow.ToString("O"), description = "Initial Schema" },
                        transaction: tx);

                    await tx.CommitAsync(ct);
                    _log.LogInformation("Applied migration V1 (Initial Schema)");
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            if (currentVersion < 2)
            {
                await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
                try
                {
                    await connection.ExecuteAsync(@"
                        ALTER TABLE plays ADD COLUMN sync_status TEXT NOT NULL DEFAULT 'Synced';
                        ALTER TABLE plays ADD COLUMN synced_at TEXT;
                        CREATE INDEX IF NOT EXISTS idx_plays_sync_status ON plays(sync_status);",
                        transaction: tx);

                    await connection.ExecuteAsync(
                        "INSERT INTO schema_migrations (version, applied_at, description) VALUES (@version, @appliedAt, @description);",
                        new { version = 2, appliedAt = DateTime.UtcNow.ToString("O"), description = "Add sync tracking columns" },
                        transaction: tx);

                    await tx.CommitAsync(ct);
                    _log.LogInformation("Applied migration V2 (Add sync tracking columns)");
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            ApplyPragmas(connection);
            return connection;
        }

        public async Task<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            ApplyPragmas(connection);
            return connection;
        }

        public async Task ExecuteInTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action, CancellationToken ct = default)
        {
            await using var connection = await CreateConnectionAsync(ct);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            try
            {
                await action(connection, transaction);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action, CancellationToken ct = default)
        {
            await using var connection = await CreateConnectionAsync(ct);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            try
            {
                var result = await action(connection, transaction);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public void Dispose()
        {
            _keepAliveMemoryConnection?.Dispose();
            _initLock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_keepAliveMemoryConnection != null)
            {
                await _keepAliveMemoryConnection.DisposeAsync();
            }
            _initLock.Dispose();
        }
    }
}
