using Circle_Tracker.Storage;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.StorageTests;

public class SqliteDatabaseManagerTests
{
    private static string NewTempDbPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.db");
    }

    private static void DeleteDbFiles(string dbPath)
    {
        foreach (string path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private static void DeleteCorruptFiles(string directory)
    {
        try
        {
            foreach (string path in Directory.GetFiles(directory, "circle_tracker_corrupt_*.db*"))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task InitializeAsync_FreshDatabase_MigratesToLatest()
    {
        string dbPath = NewTempDbPath("ct_fresh");

        try
        {
            await using var dbManager = new SqliteDatabaseManager(dbPath);

            await dbManager.InitializeAsync();

            await using var conn = await dbManager.CreateConnectionAsync();
            int version = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");

            version.Should().Be(2);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_SecondRunIsNoOp()
    {
        string dbPath = NewTempDbPath("ct_twice");

        try
        {
            await using (var first = new SqliteDatabaseManager(dbPath))
            {
                await first.InitializeAsync();
            }

            await using (var second = new SqliteDatabaseManager(dbPath))
            {
                await second.InitializeAsync();
            }

            await using var verify = new SqliteConnection($"Data Source={dbPath}");
            await verify.OpenAsync();
            int syncedAtCount = await verify.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('plays') WHERE name = 'synced_at';");

            syncedAtCount.Should().Be(1);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_PartiallyApplied_CompletesWithoutDuplicateColumnError()
    {
        string dbPath = NewTempDbPath("ct_partial");

        try
        {
            await using (var setup = new SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();

                await setup.ExecuteAsync("CREATE TABLE sessions (id TEXT PRIMARY KEY, start_time TEXT NOT NULL);");
                await setup.ExecuteAsync(@"CREATE TABLE plays (
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
                    detected_client TEXT NOT NULL
                );");
                await setup.ExecuteAsync(@"CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL,
                    description TEXT NOT NULL
                );");
                await setup.ExecuteAsync(
                    "INSERT INTO schema_migrations (version, applied_at, description) VALUES (1, '2026-01-01T00:00:00Z', 'Initial Schema');");
                await setup.ExecuteAsync("ALTER TABLE plays ADD COLUMN sync_status TEXT NOT NULL DEFAULT 'Synced';");
            }

            await using (var dbManager = new SqliteDatabaseManager(dbPath))
            {
                await dbManager.InitializeAsync();
            }

            await using var verify = new SqliteConnection($"Data Source={dbPath}");
            await verify.OpenAsync();
            int syncedAtCount = await verify.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('plays') WHERE name = 'synced_at';");

            syncedAtCount.Should().Be(1);
        }
        finally
        {
            DeleteDbFiles(dbPath);
        }
    }

    [Fact]
    public void RotateCorruptDatabase_WhenWalFilesExist_MovesAllSiblings()
    {
        string dbPath = NewTempDbPath("ct_rotate");
        string directory = Path.GetDirectoryName(dbPath) ?? Path.GetTempPath();

        try
        {
            File.WriteAllBytes(dbPath, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(dbPath + "-wal", new byte[] { 4, 5, 6 });
            File.WriteAllBytes(dbPath + "-shm", new byte[] { 7, 8, 9 });

            using var dbManager = new SqliteDatabaseManager(dbPath);

            dbManager.RotateCorruptDatabase();

            bool allMoved = !File.Exists(dbPath)
                && !File.Exists(dbPath + "-wal")
                && !File.Exists(dbPath + "-shm")
                && Directory.GetFiles(directory, "circle_tracker_corrupt_*.db*").Length == 3;

            allMoved.Should().BeTrue();
        }
        finally
        {
            DeleteDbFiles(dbPath);
            DeleteCorruptFiles(directory);
        }
    }

    [Fact]
    public void RotateCorruptDatabase_WhenDatabaseMissing_DoesNotThrow()
    {
        string dbPath = NewTempDbPath("ct_missing");

        using var dbManager = new SqliteDatabaseManager(dbPath);

        Action act = () => dbManager.RotateCorruptDatabase();

        act.Should().NotThrow();
    }
}
