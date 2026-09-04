using Circle_Tracker.Storage;
using Dapper;
using FluentAssertions;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.SessionTests;

public class SessionManagerTeardownTests
{
    [Fact]
    public async Task DisposeAsync_WhenLockHeldConcurrently_AbortsGracefullyOnCancellationWithoutHanging()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();

        var sessionManager = new SessionManager(dbManager);
        await sessionManager.InitializeAsync();

        await sessionManager._lock.WaitAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await sessionManager.DisposeAsync(cts.Token);

        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200);
    }

    [Fact]
    public async Task EndSessionAsync_CalledMultipleTimesConcurrently_ExecutesIdempotently()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();

        var sessionManager = new SessionManager(dbManager);
        await sessionManager.InitializeAsync();

        await sessionManager.IncrementPlaysAsync();
        await sessionManager.IncrementPlaysAsync();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => sessionManager.EndSessionAsync())
            .ToArray();

        var act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        sessionManager.EndTimeUtc.Should().NotBeNull();

        await using var conn = await dbManager.CreateConnectionAsync();
        var rows = (await conn.QueryAsync<dynamic>(
            "SELECT * FROM sessions WHERE id = @Id",
            new { Id = sessionManager.SessionId }
        )).ToList();

        rows.Should().HaveCount(1);
        ((string)rows[0].end_time).Should().NotBeNullOrWhiteSpace();
        ((long)rows[0].total_plays).Should().Be(2);
    }

    [Fact]
    public async Task DisposeAsync_FlushesSessionDataToDatabaseCorrectly()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();

        var sessionManager = new SessionManager(dbManager);
        await sessionManager.InitializeAsync();

        await sessionManager.IncrementPlaysAsync();
        await sessionManager.IncrementPlaysAsync();
        await sessionManager.IncrementPlaysAsync();
        await sessionManager.UpdateStatsAsync(180, 60, "v1.2.3");

        await sessionManager.DisposeAsync();

        sessionManager.EndTimeUtc.Should().NotBeNull();

        await using var conn = await dbManager.CreateConnectionAsync();
        var rows = (await conn.QueryAsync<dynamic>(
            "SELECT * FROM sessions WHERE id = @Id",
            new { Id = sessionManager.SessionId }
        )).ToList();

        rows.Should().HaveCount(1);
        var row = rows[0];
        ((string)row.end_time).Should().NotBeNullOrWhiteSpace();
        ((long)row.total_plays).Should().Be(3);
        ((long)row.playing_seconds).Should().Be(180);
        ((long)row.idle_seconds).Should().Be(60);
        ((string)row.client_version).Should().Be("v1.2.3");
    }

    [Fact]
    public async Task Dispose_WhenLockHeldConcurrently_AbortsGracefullyWithoutBlocking()
    {
        string connStr = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var dbManager = new SqliteDatabaseManager(connStr);
        await dbManager.InitializeAsync();

        var sessionManager = new SessionManager(dbManager);
        await sessionManager.InitializeAsync();

        await sessionManager._lock.WaitAsync();

        var stopwatch = Stopwatch.StartNew();
        sessionManager.Dispose();
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200);
    }
}
