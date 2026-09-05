using Circle_Tracker;
using Circle_Tracker.Storage;
using CircleTracker.Tests.Mocks;
using Dapper;
using FluentAssertions;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.IntegrationTests
{
    public class TosuTrackerIntegrationTests : IAsyncLifetime
    {
        private MockTosuWebSocketServer _server = null!;
        private SqliteDatabaseManager _db = null!;
        private LocalSqlitePlaySink _playSink = null!;
        private SessionManager _sessionManager = null!;
        private TosuClient _client = null!;
        private Mock<IMainWindow> _mockWindow = null!;
        private Tracker _tracker = null!;

        public async Task InitializeAsync()
        {
            _server = new MockTosuWebSocketServer();

            string connectionString = $"Data Source=TestDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _db = new SqliteDatabaseManager(connectionString);
            await _db.InitializeAsync();

            _playSink = new LocalSqlitePlaySink(_db);
            await _playSink.InitializeAsync();

            _sessionManager = new SessionManager(_db);
            await _sessionManager.InitializeAsync();

            _mockWindow = new Mock<IMainWindow>();
            _client = new TosuClient
            {
                Host = "127.0.0.1",
                Port = _server.Port
            };

            _tracker = new Tracker(_mockWindow.Object, _client, _playSink, _sessionManager);
            _client.StateUpdated += (_, _) => _tracker.Tick();

            await _client.ConnectAsync();
            await _server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(3));
            await WaitForConditionAsync(() => Task.FromResult(_client.IsConnected), TimeSpan.FromSeconds(3));
        }

        public async Task DisposeAsync()
        {
            await _tracker.FlushPendingSubmissionsAsync();
            await _client.DisconnectAsync();
            _client.Dispose();
            await _playSink.DisposeAsync();
            await _server.DisposeAsync();
            await _db.DisposeAsync();
        }

        private static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return;
                }
                await Task.Delay(25);
            }

            throw new TimeoutException("Condition was not satisfied within timeout.");
        }

        [Fact]
        public async Task Should_RecordCompletedPlayInDatabase_When_FullPlayCycleExecuted()
        {
            const int beatmapId = 123456;
            const int beatmapSetId = 654321;
            const string checksum = "a1b2c3d4e5f6";
            const string title = "Integration Song";
            const string artist = "Test Artist";
            const string version = "Expert";
            const int mods = 8;
            const int h300 = 40;
            const int h100 = 10;
            const int totalHits = 50;
            const decimal accuracy = 97.5m;

            var songSelectState = StateBuilder.Build(
                gameStateNumber: 5,
                h300: 0,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 0,
                accuracy: 0,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                mods: mods,
                title: title,
                artist: artist,
                version: version,
                beatmapId: beatmapId,
                beatmapSetId: beatmapSetId);

            var playingState1 = StateBuilder.Build(
                gameStateNumber: 2,
                h300: 20,
                h100: 5,
                h50: 0,
                misses: 0,
                songTimeMs: 15000,
                accuracy: 98m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                mods: mods,
                title: title,
                artist: artist,
                version: version,
                beatmapId: beatmapId,
                beatmapSetId: beatmapSetId);

            var playingState2 = StateBuilder.Build(
                gameStateNumber: 2,
                h300: h300,
                h100: h100,
                h50: 0,
                misses: 0,
                songTimeMs: 30000,
                accuracy: accuracy,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                mods: mods,
                title: title,
                artist: artist,
                version: version,
                beatmapId: beatmapId,
                beatmapSetId: beatmapSetId);

            var resultsState = StateBuilder.Build(
                gameStateNumber: 7,
                h300: h300,
                h100: h100,
                h50: 0,
                misses: 0,
                songTimeMs: 30000,
                accuracy: accuracy,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                mods: mods,
                title: title,
                artist: artist,
                version: version,
                beatmapId: beatmapId,
                beatmapSetId: beatmapSetId);

            await _server.BroadcastStateAsync(songSelectState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 5), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(playingState1);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 2), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(playingState2);
            await WaitForConditionAsync(() => Task.FromResult((_client.LatestState?.Play?.Hits?.H300 ?? 0) == h300), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(resultsState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 7), TimeSpan.FromSeconds(3));

            await WaitForConditionAsync(async () =>
            {
                using var conn = await _db.CreateConnectionAsync();
                var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");
                return count == 1;
            }, TimeSpan.FromSeconds(3));

            await _tracker.FlushPendingSubmissionsAsync();

            using var verifyConn = await _db.CreateConnectionAsync();
            var plays = (await verifyConn.QueryAsync<dynamic>("SELECT * FROM plays;")).ToList();

            plays.Should().HaveCount(1);
            var record = plays[0];
            ((int)record.is_complete).Should().Be(1);
            ((int)record.beatmap_id).Should().Be(beatmapId);
            ((string)record.beatmap_checksum).Should().Be(checksum);
            ((int)record.mods_bitfield).Should().Be(mods);
            ((int)record.total_hits).Should().Be(totalHits);
            ((int)record.hit_300).Should().Be(h300);
            ((int)record.hit_100).Should().Be(h100);
            _sessionManager.TotalPlays.Should().Be(1);
        }

        [Fact]
        public async Task Should_RecordIncompletePlayAndResetCounters_When_QuickRetryTriggered()
        {
            const int beatmapId = 222333;
            const string checksum = "retry_test_checksum";
            const int h300 = 45;

            var playingState1 = StateBuilder.Build(
                gameStateNumber: 2,
                h300: 20,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 20000,
                accuracy: 100m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                beatmapId: beatmapId);

            var playingState2 = StateBuilder.Build(
                gameStateNumber: 2,
                h300: h300,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 40000,
                accuracy: 100m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                beatmapId: beatmapId);

            var retriedPlayingState = StateBuilder.Build(
                gameStateNumber: 2,
                h300: 0,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 1500,
                accuracy: 0m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                beatmapId: beatmapId);

            await _server.BroadcastStateAsync(playingState1);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.Beatmap?.Time?.Live == 20000), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(playingState2);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.Beatmap?.Time?.Live == 40000), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(retriedPlayingState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.Beatmap?.Time?.Live == 1500), TimeSpan.FromSeconds(3));

            await WaitForConditionAsync(async () =>
            {
                using var conn = await _db.CreateConnectionAsync();
                var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");
                return count == 1;
            }, TimeSpan.FromSeconds(3));

            await _tracker.FlushPendingSubmissionsAsync();

            using var verifyConn = await _db.CreateConnectionAsync();
            var plays = (await verifyConn.QueryAsync<dynamic>("SELECT * FROM plays;")).ToList();

            plays.Should().HaveCount(1);
            var record = plays[0];
            ((int)record.is_complete).Should().Be(0);
            ((int)record.total_hits).Should().Be(h300);
            ((int)record.beatmap_id).Should().Be(beatmapId);
            ((string)record.beatmap_checksum).Should().Be(checksum);

            var snapshot = _tracker.GetSnapshot();
            snapshot.Play300c.Should().Be(0);
            snapshot.TotalBeatmapHits.Should().Be(0);
        }

        [Fact]
        public async Task Should_SuppressPlayLogging_When_ReplayFlagDetected()
        {
            const int beatmapId = 888999;
            const string checksum = "replay_checksum";

            var replayPlayingState = StateBuilder.Build(
                gameStateNumber: 2,
                h300: 45,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 30000,
                accuracy: 100m,
                playerName: "SpectatedPlayer",
                profileName: "LocalUser",
                checksum: checksum,
                beatmapId: beatmapId);

            replayPlayingState.Settings ??= new TosuSettings();
            replayPlayingState.Settings.Client ??= new TosuClientInfo();
            replayPlayingState.Settings.Client.Version = "lazer";
            replayPlayingState.Settings.ReplayUIVisible = true;

            var replayResultsState = StateBuilder.Build(
                gameStateNumber: 7,
                h300: 45,
                h100: 0,
                h50: 0,
                misses: 0,
                songTimeMs: 30000,
                accuracy: 100m,
                playerName: "SpectatedPlayer",
                profileName: "LocalUser",
                checksum: checksum,
                beatmapId: beatmapId);

            replayResultsState.Settings ??= new TosuSettings();
            replayResultsState.Settings.Client ??= new TosuClientInfo();
            replayResultsState.Settings.Client.Version = "lazer";
            replayResultsState.Settings.ReplayUIVisible = true;

            await _server.BroadcastStateAsync(replayPlayingState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 2), TimeSpan.FromSeconds(3));

            _tracker.GetSnapshot().IsReplay.Should().BeTrue();

            await _server.BroadcastStateAsync(replayResultsState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 7), TimeSpan.FromSeconds(3));

            await _tracker.FlushPendingSubmissionsAsync();

            using var verifyConn = await _db.CreateConnectionAsync();
            var playCount = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");

            playCount.Should().Be(0);
            _sessionManager.TotalPlays.Should().Be(0);
        }

        [Fact]
        public async Task Should_NotSubmitPlay_When_HitsBelowMinimumThreshold()
        {
            const int beatmapId = 333444;
            const string checksum = "low_hits_checksum";

            var playingState = StateBuilder.Build(
                gameStateNumber: 2,
                h300: 10,
                h100: 2,
                h50: 0,
                misses: 0,
                songTimeMs: 10000,
                accuracy: 100m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                beatmapId: beatmapId);

            var resultsState = StateBuilder.Build(
                gameStateNumber: 7,
                h300: 10,
                h100: 2,
                h50: 0,
                misses: 0,
                songTimeMs: 10000,
                accuracy: 100m,
                playerName: "testplayer",
                profileName: "testplayer",
                checksum: checksum,
                beatmapId: beatmapId);

            await _server.BroadcastStateAsync(playingState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 2), TimeSpan.FromSeconds(3));

            await _server.BroadcastStateAsync(resultsState);
            await WaitForConditionAsync(() => Task.FromResult(_client.LatestState?.State?.Number == 7), TimeSpan.FromSeconds(3));

            await _tracker.FlushPendingSubmissionsAsync();

            using var verifyConn = await _db.CreateConnectionAsync();
            var count = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM plays;");

            count.Should().Be(0);
            _sessionManager.TotalPlays.Should().Be(0);
        }
    }
}
