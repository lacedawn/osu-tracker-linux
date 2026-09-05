using Circle_Tracker;
using FluentAssertions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests
{
    public class TrackerTests
    {
        [Fact]
        public async Task Tick_WhenTransitionFromPlayingToResults_SubmitsCompleteEntry()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 45, songTimeMs: 30000));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 45));
            tracker.Tick();
            await Task.Delay(100);

            sink.Verify(s => s.TryAppendPlayEntry(
                It.Is<PlayEntryData>(d => d.Complete == true && d.TotalBeatmapHits >= 40),
                false, It.IsAny<int>(), 0,
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [Fact]
        public async Task Tick_WhenRetryDetected_SubmitsIncompleteEntry()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 45, songTimeMs: 30000));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 0, songTimeMs: 500));
            tracker.Tick();
            await Task.Delay(100);

            sink.Verify(s => s.TryAppendPlayEntry(
                It.Is<PlayEntryData>(d => d.Complete == false),
                false, It.IsAny<int>(), 0,
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [Fact]
        public void Tick_WhenHitsBelowMinimum_DoesNotSubmit()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 10, songTimeMs: 5000));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 10));
            tracker.Tick();

            sink.Verify(s => s.TryAppendPlayEntry(
                It.IsAny<PlayEntryData>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Never);
        }

        [Fact]
        public void Tick_WhenPlayerNameDifferentFromProfile_DetectsReplayAndDoesNotSubmit()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(
                StateBuilder.Playing(h300: 100, songTimeMs: 60000, playerName: "SomeOtherPlayer", profileName: "testplayer"));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Build(
                gameStateNumber: 7, h300: 100, songTimeMs: 60000, 
                playerName: "SomeOtherPlayer", profileName: "testplayer"));
            tracker.Tick();

            sink.Verify(s => s.TryAppendPlayEntry(
                It.IsAny<PlayEntryData>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Never);
        }

        [Fact]
        public void Tick_WhenClientDisconnected_DoesNotCrash()
        {
            var (tracker, client, _) = TrackerFactory.Create(connected: false);

            var act = () => tracker.Tick();

            act.Should().NotThrow();
        }

        [Fact]
        public async Task Tick_ConsecutivePlayCount_IncrementsForSameMapSameMods()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 45, songTimeMs: 30000, checksum: "map1"));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 0, songTimeMs: 500, checksum: "map1"));
            tracker.Tick();
            await Task.Delay(100);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 45, songTimeMs: 30000, checksum: "map1"));
            tracker.Tick();
            client.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 45, checksum: "map1"));
            tracker.Tick();
            await Task.Delay(100);

            sink.Verify(s => s.TryAppendPlayEntry(
                It.Is<PlayEntryData>(d => d.PlayCount == 2),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [Theory]
        [InlineData(0,   false, false, false, false, false, false)]
        [InlineData(8,   true,  false, false, false, false, false)]
        [InlineData(24,  true,  true,  false, false, false, false)]
        [InlineData(72,  true,  false, true,  false, false, false)]
        [InlineData(2,   false, false, false, true,  false, false)]
        [InlineData(256, false, false, false, false, true,  false)]
        [InlineData(1024,false, false, false, false, false, true )]
        public async Task Tick_ModParsing_SetsModFlagsCorrectly(int rawMods,
            bool expectedHD, bool expectedHR, bool expectedDT,
            bool expectedEZ, bool expectedHT, bool expectedFL)
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 45, mods: rawMods));
            tracker.Tick();
            client.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 45));
            tracker.Tick();
            await Task.Delay(100);

            sink.Verify(s => s.TryAppendPlayEntry(
                It.Is<PlayEntryData>(d =>
                    d.Hidden == expectedHD &&
                    d.Hardrock == expectedHR &&
                    d.Doubletime == expectedDT &&
                    d.EZ == expectedEZ &&
                    d.Halftime == expectedHT &&
                    d.Flashlight == expectedFL),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [Fact]
        public void Tick_WhenHitJumpExceedsMax_ButShortTimeDelta_IgnoresStaleData()
        {
            var (tracker, client, sink) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 10, songTimeMs: 5000));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 200, songTimeMs: 5100));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Results(h300: 10));
            tracker.Tick();

            sink.Verify(s => s.TryAppendPlayEntry(
                It.IsAny<PlayEntryData>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<DateTime>(), It.IsAny<Action<DateTime>>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()
            ), Times.Never);
        }

        [Fact]
        public void GetSnapshot_WhenPlaying_ReturnsCorrectMetadataAndGameStateLabel()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(
                title: "CANDYYYLAND feat. LIZ",
                artist: "tofubeats",
                version: "Pa's Lam System Remix",
                beatmapId: 12345,
                beatmapSetId: 67890,
                hp: 5.5m));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();

            snapshot.BeatmapTitle.Should().Be("CANDYYYLAND feat. LIZ");
            snapshot.BeatmapArtist.Should().Be("tofubeats");
            snapshot.BeatmapVersion.Should().Be("Pa's Lam System Remix");
            snapshot.BeatmapId.Should().Be(12345);
            snapshot.BeatmapSetId.Should().Be(67890);
            snapshot.BeatmapHp.Should().Be(5.5m);
            snapshot.GameStateLabel.Should().Be("PLAYING");
            snapshot.CoverUrl.Should().Be("https://assets.ppy.sh/beatmaps/67890/covers/cover.jpg");
        }

        [Fact]
        public void GetSnapshot_WhenResultsScreen_ReturnsResultsLabel()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Results());
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();

            snapshot.GameStateLabel.Should().Be("RESULTS");
        }

        [Fact]
        public void GetSnapshot_WhenReplay_ReturnsReplayLabel()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(
                playerName: "OtherPlayer",
                profileName: "testplayer"));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();

            snapshot.GameStateLabel.Should().Be("REPLAY");
        }

        [Fact]
        public void GetSnapshot_WhenMenuState_ReturnsIdleLabel()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Build(gameStateNumber: 0));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();

            snapshot.GameStateLabel.Should().Be("IDLE");
        }

        [Fact]
        public void GetSnapshot_WhenBeatmapSetIdZero_ReturnsEmptyCoverUrl()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(beatmapSetId: 0));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();

            snapshot.CoverUrl.Should().BeEmpty();
        }

        [Fact]
        public void Should_AcceptHitCountJump_When_SongTimeAdvancedProportionally()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 40, songTimeMs: 10000));
            tracker.Tick();

            var snapshot1 = tracker.GetSnapshot();
            snapshot1.TotalBeatmapHits.Should().Be(40);
            snapshot1.Accuracy.Should().Be(0);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 110, songTimeMs: 12000, accuracy: 95.5m));
            tracker.Tick();

            var snapshot2 = tracker.GetSnapshot();
            snapshot2.TotalBeatmapHits.Should().Be(110);
            snapshot2.Accuracy.Should().Be(95.5m);
            snapshot2.Play300c.Should().Be(110);
        }

        [Fact]
        public void Should_ContinueTrackingHitsNormally_After_LargeHitJump()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 40, songTimeMs: 10000));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 110, songTimeMs: 12000, accuracy: 95.5m));
            tracker.Tick();

            var snapshot1 = tracker.GetSnapshot();
            snapshot1.TotalBeatmapHits.Should().Be(110);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 115, songTimeMs: 12100, accuracy: 96.0m));
            tracker.Tick();

            var snapshot2 = tracker.GetSnapshot();
            snapshot2.TotalBeatmapHits.Should().Be(115);
            snapshot2.Accuracy.Should().Be(96.0m);
            snapshot2.Play300c.Should().Be(115);
        }

        [Fact]
        public void Should_RejectNegativeOrCorruptHitJumps()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 50, songTimeMs: 10000, accuracy: 98.0m));
            tracker.Tick();

            var snapshot1 = tracker.GetSnapshot();
            snapshot1.TotalBeatmapHits.Should().Be(50);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 5000, songTimeMs: 10100, accuracy: 99.0m));
            tracker.Tick();

            var snapshot2 = tracker.GetSnapshot();
            snapshot2.TotalBeatmapHits.Should().Be(50);
            snapshot2.Accuracy.Should().Be(98.0m);
        }

        [Fact]
        public void Should_AcceptLargeHitJump_When_LongTimeElapsed()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 30, songTimeMs: 8000, accuracy: 99.0m));
            tracker.Tick();

            var snapshot1 = tracker.GetSnapshot();
            snapshot1.TotalBeatmapHits.Should().Be(30);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 150, songTimeMs: 12000, accuracy: 97.0m));
            tracker.Tick();

            var snapshot2 = tracker.GetSnapshot();
            snapshot2.TotalBeatmapHits.Should().Be(150);
            snapshot2.Accuracy.Should().Be(97.0m);
        }

        [Fact]
        public void Should_UpdateMissCount_Monotonically()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 40, misses: 2, songTimeMs: 10000));
            tracker.Tick();

            var snapshot1 = tracker.GetSnapshot();
            snapshot1.PlayMissc.Should().Be(2);

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 110, misses: 5, songTimeMs: 12000, accuracy: 95.5m));
            tracker.Tick();

            var snapshot2 = tracker.GetSnapshot();
            snapshot2.PlayMissc.Should().Be(5);
        }

        [Fact]
        public void Should_RejectHitJump_When_ShortTimeElapsed()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 30, songTimeMs: 8000, accuracy: 99.0m));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 150, songTimeMs: 8200, accuracy: 97.0m));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();
            snapshot.TotalBeatmapHits.Should().Be(30);
            snapshot.Accuracy.Should().Be(99.0m);
        }

        [Fact]
        public void Should_AcceptSmallHitJump_Regardless_Of_TimeElapsed()
        {
            var (tracker, client, _) = TrackerFactory.Create();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 30, songTimeMs: 8000, accuracy: 99.0m));
            tracker.Tick();

            client.Setup(c => c.LatestState).Returns(StateBuilder.Playing(h300: 35, songTimeMs: 8100, accuracy: 98.5m));
            tracker.Tick();

            var snapshot = tracker.GetSnapshot();
            snapshot.TotalBeatmapHits.Should().Be(35);
            snapshot.Accuracy.Should().Be(98.5m);
        }
    }
}
