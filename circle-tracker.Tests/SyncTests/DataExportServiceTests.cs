using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.Sync;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.SyncTests;

public class DataExportServiceTests
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

        try
        {
            string directory = Path.GetDirectoryName(dbPath) ?? Path.GetTempPath();
            string fileName = Path.GetFileName(dbPath);
            foreach (string temp in Directory.GetFiles(directory, fileName + ".tmp-*"))
            {
                try
                {
                    File.Delete(temp);
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
    public async Task BackupDatabaseAsync_ValidDatabase_CreatesBackupFile()
    {
        string sourcePath = NewTempDbPath("ct_src");
        string backupPath = NewTempDbPath("ct_backup");

        try
        {
            await using var dbManager = new SqliteDatabaseManager(sourcePath);
            await dbManager.InitializeAsync();

            var queryEngine = new Mock<IPlayQueryEngine>();
            var service = new DataExportService(dbManager, queryEngine.Object);

            await service.BackupDatabaseAsync(backupPath);

            int backupVersion;
            await using (var backupConn = new SqliteConnection($"Data Source={backupPath}"))
            {
                await backupConn.OpenAsync();
                backupVersion = await backupConn.ExecuteScalarAsync<int>(
                    "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
            }

            backupVersion.Should().Be(4);
        }
        finally
        {
            DeleteDbFiles(sourcePath);
            DeleteDbFiles(backupPath);
        }
    }

    [Fact]
    public async Task BackupDatabaseAsync_WhenSourceFails_PreservesPreviousBackup()
    {
        string backupPath = NewTempDbPath("ct_backup_fail");
        byte[] originalBytes = new byte[] { 10, 20, 30, 40, 50 };

        try
        {
            await File.WriteAllBytesAsync(backupPath, originalBytes);

            var dbManager = new Mock<IDatabaseManager>();
            dbManager.Setup(m => m.CreateConnectionAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("source failure"));

            var queryEngine = new Mock<IPlayQueryEngine>();
            var service = new DataExportService(dbManager.Object, queryEngine.Object);

            try
            {
                await service.BackupDatabaseAsync(backupPath);
            }
            catch (InvalidOperationException)
            {
            }

            byte[] currentBytes = await File.ReadAllBytesAsync(backupPath);

            currentBytes.Should().BeEquivalentTo(originalBytes);
        }
        finally
        {
            DeleteDbFiles(backupPath);
        }
    }
}
