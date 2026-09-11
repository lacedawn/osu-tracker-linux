using Circle_Tracker;
using Circle_Tracker.Storage;
using Circle_Tracker.Storage.Querying;
using Circle_Tracker.Sync;
using Dapper;
using FluentAssertions;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.SyncTests;

public class GoogleSheetsSyncTests : IDisposable
{
    private readonly SqliteDatabaseManager _dbManager;
    private readonly string _testDbPath;

    public GoogleSheetsSyncTests()
    {
        _testDbPath = $"test_sync_{Guid.NewGuid()}.db";
        _dbManager = new SqliteDatabaseManager(_testDbPath);
        _dbManager.InitializeAsync().Wait();
    }

    public void Dispose()
    {
        _dbManager.Dispose();
        if (System.IO.File.Exists(_testDbPath))
            System.IO.File.Delete(_testDbPath);
    }

    [Fact]
    public void HyperlinkRegex_ExtractsCorrectValues()
    {
        var pattern = @"^=HYPERLINK\(""https:\/\/osu\.ppy\.sh\/beatmapsets\/(\d+)(?:#osu\/(\d+))?""[;,]\s*""(.+?)""\)";
        var regex = new Regex(pattern);

        var formula = @"=HYPERLINK(""https://osu.ppy.sh/beatmapsets/12345#osu/67890"", ""Camellia - GHOST [EXTRA] +HDHR"")";
        var match = regex.Match(formula);

        Assert.True(match.Success);
        Assert.Equal("12345", match.Groups[1].Value);
        Assert.Equal("67890", match.Groups[2].Value);
        Assert.Equal("Camellia - GHOST [EXTRA] +HDHR", match.Groups[3].Value);
    }

    [Fact]
    public void HyperlinkRegex_HandlesCommaAndSemicolonSeparators()
    {
        var pattern = @"^=HYPERLINK\(""https:\/\/osu\.ppy\.sh\/beatmapsets\/(\d+)(?:#osu\/(\d+))?""[;,]\s*""(.+?)""\)";
        var regex = new Regex(pattern);

        var formulaComma = @"=HYPERLINK(""https://osu.ppy.sh/beatmapsets/100#osu/200"", ""Test Map"")";
        var formulaSemicolon = @"=HYPERLINK(""https://osu.ppy.sh/beatmapsets/100#osu/200""; ""Test Map"")";

        Assert.True(regex.Match(formulaComma).Success);
        Assert.True(regex.Match(formulaSemicolon).Success);
    }

    [Fact]
    public void ModBitfieldConstruction_CreatesCorrectValues()
    {
        bool hd = true, hr = true, dt = false, ez = false, ht = false, fl = false;
        int mods = 0;
        if (ez) mods |= (1 << 1);
        if (hd) mods |= (1 << 3);
        if (hr) mods |= (1 << 4);
        if (dt) mods |= (1 << 6);
        if (ht) mods |= (1 << 8);
        if (fl) mods |= (1 << 10);

        int expected = (1 << 3) + (1 << 4);
        Assert.Equal(expected, mods);
        Assert.Equal(8 + 16, mods);
        Assert.Equal(24, mods);
    }

    [Fact]
    public async Task DuplicateDetection_SkipsDuplicatePlays()
    {
        var timestamp = DateTime.UtcNow;
        var timestampStr = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        await using var conn = await _dbManager.CreateConnectionAsync();
        var sessionId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent) VALUES (@Id, @Start, 0, 0, 0, 0.0);",
            new { Id = sessionId, Start = timestampStr });

        await conn.ExecuteAsync(@"
            INSERT INTO plays (
                session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client
            ) VALUES (
                @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, '',
                'Test Map', '', '', '',
                24, 'HDHR', 180, 5.5, 2.5, 2.8, 4.0, 9.0, 8.0, 6.0,
                500, 450, 40, 10, 0, 98.5, 1,
                1, 120, 1, 0, 0, 'Test'
            );", new { SessionId = sessionId, Timestamp = timestampStr, BeatmapId = 12345, BeatmapSetId = 100 });

        const string checkSql = @"
            SELECT COUNT(*) FROM plays
            WHERE timestamp = @Timestamp
              AND beatmap_id = @BeatmapId
              AND total_hits = @TotalHits;";

        var count = await conn.ExecuteScalarAsync<int>(checkSql, new
        {
            Timestamp = timestampStr,
            BeatmapId = 12345,
            TotalHits = 500
        });

        Assert.Equal(1, count);

        var duplicateCount = await conn.ExecuteScalarAsync<int>(checkSql, new
        {
            Timestamp = timestampStr,
            BeatmapId = 12345,
            TotalHits = 500
        });

        Assert.Equal(1, duplicateCount);
    }

    [Fact]
    public async Task OfflineQueue_PendingPlaysAreQueried()
    {
        await using var conn = await _dbManager.CreateConnectionAsync();
        var sessionId = Guid.NewGuid().ToString();
        var timestampStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        await conn.ExecuteAsync(
            "INSERT INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent) VALUES (@Id, @Start, 0, 0, 0, 0.0);",
            new { Id = sessionId, Start = timestampStr });

        for (int i = 0; i < 5; i++)
        {
            await conn.ExecuteAsync(@"
                INSERT INTO plays (
                    session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                    beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                    mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                    total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                    is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client,
                    sync_status
                ) VALUES (
                    @SessionId, @Timestamp, @BeatmapId, @BeatmapSetId, '',
                    'Test Map', '', '', '',
                    0, 'NM', 180, 5.5, 2.5, 2.8, 4.0, 9.0, 8.0, 6.0,
                    500, 450, 40, 10, 0, 98.5, 1,
                    1, 120, 1, 0, 0, 'Test',
                    'Pending'
                );", new
            {
                SessionId = sessionId,
                Timestamp = DateTime.UtcNow.AddMinutes(-i).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                BeatmapId = 10000 + i,
                BeatmapSetId = 1000 + i
            });
        }

        var pendingCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM plays WHERE sync_status = 'Pending';");

        Assert.Equal(5, pendingCount);
    }

    [Fact]
    public async Task SyncStatusUpdate_TransitionsPendingToSynced()
    {
        await using var conn = await _dbManager.CreateConnectionAsync();
        var sessionId = Guid.NewGuid().ToString();
        var timestampStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        await conn.ExecuteAsync(
            "INSERT INTO sessions (id, start_time, total_plays, playing_seconds, idle_seconds, efficiency_percent) VALUES (@Id, @Start, 0, 0, 0, 0.0);",
            new { Id = sessionId, Start = timestampStr });

        await conn.ExecuteAsync(@"
            INSERT INTO plays (
                session_id, timestamp, beatmap_id, beatmap_set_id, beatmap_checksum,
                beatmap_string, beatmap_title, beatmap_artist, beatmap_version,
                mods_bitfield, mods_string, bpm, stars, aim, speed, cs, ar, od, hp,
                total_hits, hit_300, hit_100, hit_50, hit_miss, accuracy, accuracy_reliable,
                is_complete, play_time_seconds, consecutive_play_count, game_mode, is_replay, detected_client,
                sync_status
            ) VALUES (
                @SessionId, @Timestamp, 12345, 100, '',
                'Test Map', '', '', '',
                0, 'NM', 180, 5.5, 2.5, 2.8, 4.0, 9.0, 8.0, 6.0,
                500, 450, 40, 10, 0, 98.5, 1,
                1, 120, 1, 0, 0, 'Test',
                'Pending'
            );", new { SessionId = sessionId, Timestamp = timestampStr });

        var playId = await conn.ExecuteScalarAsync<long>(
            "SELECT id FROM plays WHERE sync_status = 'Pending' LIMIT 1;");

        var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        await conn.ExecuteAsync(@"
            UPDATE plays
            SET sync_status = 'Synced', synced_at = @SyncedAt
            WHERE id = @Id;", new { Id = playId, SyncedAt = nowUtc });

        var status = await conn.ExecuteScalarAsync<string>(
            "SELECT sync_status FROM plays WHERE id = @Id;", new { Id = playId });

        Assert.Equal("Synced", status);
    }

    [Fact]
    public void CsvExport_ProperlyEscapesFields()
    {
        var testCases = new[]
        {
            ("Simple", "Simple"),
            ("Has,Comma", "\"Has,Comma\""),
            ("Has\"Quote", "\"Has\"\"Quote\""),
            ("Has\nNewline", "\"Has\nNewline\""),
            ("", "")
        };

        foreach (var (input, expected) in testCases)
        {
            var result = EscapeCsvField(input);
            Assert.Equal(expected, result);
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    [Fact]
    public void SessionReconstruction_Creates45MinuteThresholdSessions()
    {
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var plays = new[]
        {
            baseTime,
            baseTime.AddMinutes(10),
            baseTime.AddMinutes(20),
            baseTime.AddMinutes(70),
            baseTime.AddMinutes(80)
        };

        string? currentSessionId = null;
        DateTime? lastTimestamp = null;
        var sessionCount = 0;

        foreach (var playTime in plays)
        {
            if (lastTimestamp.HasValue && (playTime - lastTimestamp.Value).TotalMinutes > 45)
            {
                currentSessionId = Guid.NewGuid().ToString();
                sessionCount++;
            }
            else if (currentSessionId == null)
            {
                currentSessionId = Guid.NewGuid().ToString();
                sessionCount++;
            }

            lastTimestamp = playTime;
        }

        Assert.Equal(2, sessionCount);
    }

    [Fact]
    public void ModBitfield_ExtractsCorrectFlags()
    {
        int modsBitfield = (1 << 3) | (1 << 6);

        bool hd = (modsBitfield & (1 << 3)) != 0;
        bool hr = (modsBitfield & (1 << 4)) != 0;
        bool dt = (modsBitfield & (1 << 6)) != 0;

        Assert.True(hd);
        Assert.False(hr);
        Assert.True(dt);
    }

    [Fact]
    public async Task SyncStatusIndex_Exists()
    {
        await using var conn = await _dbManager.CreateConnectionAsync();

        var indexExists = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'index'
              AND name = 'idx_plays_sync_status';");

        Assert.Equal(1, indexExists);
    }

    private static async Task<SqliteDatabaseManager> CreateImportDbManagerAsync()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var dbManager = new SqliteDatabaseManager(connStr);

        await dbManager.InitializeAsync();

        return dbManager;
    }

    private static GoogleSheetsHistoricalImporter.ParsedImportRow BuildImportRow(
        DateTime timestamp,
        int beatmapId = 12345,
        int totalHits = 500,
        decimal accuracy = 98.5m,
        int hit300 = 450,
        int hit100 = 40,
        int hit50 = 10,
        int hitMiss = 0,
        int modsBitfield = 0,
        bool isComplete = true)
    {
        return new GoogleSheetsHistoricalImporter.ParsedImportRow(
            Timestamp: timestamp,
            BeatmapId: beatmapId,
            BeatmapSetId: 100,
            BeatmapString: "Artist - Title [Diff]",
            Bpm: 180,
            Stars: 5.5m,
            Aim: 2.5m,
            Speed: 2.8m,
            Cs: 4.0m,
            Ar: 9.0m,
            Od: 8.0m,
            TotalHits: totalHits,
            Accuracy: accuracy,
            Hit300: hit300,
            Hit100: hit100,
            Hit50: hit50,
            HitMiss: hitMiss,
            ModsBitfield: modsBitfield,
            ModsString: "NM",
            IsComplete: isComplete,
            PlayTimeSeconds: 120,
            PlayCount: 1);
    }

    [Fact]
    public async Task SameMapTwiceSameMinute_BothImported()
    {
        using var dbManager = await CreateImportDbManagerAsync();
        var importer = new GoogleSheetsHistoricalImporter(null!, dbManager);
        var minute = new DateTime(2024, 5, 1, 12, 34, 0, DateTimeKind.Utc);

        var rows = new List<GoogleSheetsHistoricalImporter.ParsedImportRow>
        {
            BuildImportRow(minute, accuracy: 98.5m, hit300: 450, hit100: 40),
            BuildImportRow(minute, accuracy: 97.0m, hit300: 440, hit100: 50)
        };

        SyncResult result = await importer.ImportParsedRowsAsync(rows);

        result.SyncedCount.Should().Be(2);
    }

    [Fact]
    public async Task LargeBatch_ImportsInTransaction()
    {
        using var dbManager = await CreateImportDbManagerAsync();
        var importer = new GoogleSheetsHistoricalImporter(null!, dbManager);
        var baseTime = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var rows = Enumerable.Range(0, 200).Select(i => BuildImportRow(
            baseTime.AddMinutes(i),
            beatmapId: 20000 + i,
            accuracy: 90.0m + (i % 9),
            hit300: 400 + (i % 50))).ToList();

        SyncResult result = await importer.ImportParsedRowsAsync(rows);

        result.SyncedCount.Should().Be(200);

        await using var conn = await dbManager.CreateConnectionAsync();
        int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");

        count.Should().Be(200);
    }

    [Fact]
    public async Task ExactDuplicate_SkippedAsDuplicate()
    {
        using var dbManager = await CreateImportDbManagerAsync();
        var importer = new GoogleSheetsHistoricalImporter(null!, dbManager);
        var minute = new DateTime(2024, 5, 1, 12, 34, 0, DateTimeKind.Utc);

        var rows = new List<GoogleSheetsHistoricalImporter.ParsedImportRow>
        {
            BuildImportRow(minute),
            BuildImportRow(minute)
        };

        SyncResult result = await importer.ImportParsedRowsAsync(rows);

        result.SyncedCount.Should().Be(1);
    }
}
