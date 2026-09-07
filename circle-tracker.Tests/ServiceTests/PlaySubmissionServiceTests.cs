using Circle_Tracker;
using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using FluentAssertions;
using Moq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class PlaySubmissionServiceTests
{
    [Fact]
    public async Task TryPostBeatmapEntry_BelowMinHits_DoesNotSubmit()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 39);
        await service.FlushPendingSubmissionsAsync();

        sink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryPostBeatmapEntry_IsReplay_DoesNotSubmit()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(true);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        await service.FlushPendingSubmissionsAsync();

        sink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryPostBeatmapEntry_NonStandardGameMode_DoesNotSubmit()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50, currentGameMode: 1);
        await service.FlushPendingSubmissionsAsync();

        sink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryPostBeatmapEntry_ValidPlay_SubmitsToSink()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        await service.FlushPendingSubmissionsAsync();

        sink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void TryPostBeatmapEntry_SameMapReplay_IncrementsPlayCount()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        beatmapState.Setup(b => b.CurrentBeatmapChecksum).Returns("checksum1");
        beatmapState.Setup(b => b.BeatmapID).Returns(12345);
        beatmapState.Setup(b => b.BeatmapString).Returns("Artist - Title [Diff]");
        beatmapState.Setup(b => b.RawMods).Returns(0);
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: false, totalBeatmapHits: 50);
        int firstPlayCount = service.ConsecutivePlayCount;
        service.TryPostBeatmapEntry(complete: false, totalBeatmapHits: 50);

        firstPlayCount.Should().Be(1);
        service.ConsecutivePlayCount.Should().Be(2);
    }

    [Fact]
    public void TryPostBeatmapEntry_DifferentMap_ResetsPlayCount()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        beatmapState.SetupSequence(b => b.CurrentBeatmapChecksum)
            .Returns("checksum1")
            .Returns("checksum2");
        beatmapState.SetupSequence(b => b.BeatmapID)
            .Returns(101)
            .Returns(102);
        beatmapState.SetupSequence(b => b.BeatmapString)
            .Returns("Map 1")
            .Returns("Map 2");
        beatmapState.Setup(b => b.RawMods).Returns(0);
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: false, totalBeatmapHits: 50);
        service.TryPostBeatmapEntry(complete: false, totalBeatmapHits: 50);

        service.ConsecutivePlayCount.Should().Be(1);
    }

    [Fact]
    public async Task FlushPendingSubmissions_WaitsForAllTasks()
    {
        var tcs = new TaskCompletionSource<bool>();
        var sink = new Mock<IPlaySink>();
        sink.Setup(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        var flushTask = service.FlushPendingSubmissionsAsync();

        flushTask.IsCompleted.Should().BeFalse();
        tcs.SetResult(true);
        await flushTask;
        flushTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task FlushPendingSubmissions_ConcurrentSubmissions_NoRaceCondition()
    {
        var sink = new Mock<IPlaySink>();
        sink.Setup(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(async () => await Task.Yield());

        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        })).ToArray();

        await Task.WhenAll(tasks);
        await service.FlushPendingSubmissionsAsync();

        sink.Verify(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(50));
    }

    [Fact]
    public async Task PlayLogged_EventFired_AfterSuccessfulSubmission()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        (PlayEntryData Data, PlayContext Context)? loggedEvent = null;
        service.PlayLogged += (_, args) => loggedEvent = args;

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        await service.FlushPendingSubmissionsAsync();

        loggedEvent.Should().NotBeNull();
        loggedEvent!.Value.Data.Complete.Should().BeTrue();
        loggedEvent!.Value.Data.TotalBeatmapHits.Should().Be(50);
    }

    [Fact]
    public async Task FlushPendingSubmissionsAsync_WaitsForInFlightSubmission_BeforeReturning()
    {
        var sink = new Mock<IPlaySink>();
        sink.Setup(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(() => Task.Delay(TimeSpan.FromMilliseconds(200)));
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        var stopwatch = Stopwatch.StartNew();
        await service.FlushPendingSubmissionsAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task FlushPendingSubmissionsAsync_WithNoActiveTasks_ReturnsImmediately()
    {
        var sink = new Mock<IPlaySink>();
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);

        var stopwatch = Stopwatch.StartNew();
        await service.FlushPendingSubmissionsAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FlushPendingSubmissionsAsync_CancellationRequested_DoesNotThrow()
    {
        var sink = new Mock<IPlaySink>();
        sink.Setup(s => s.TryLogPlayAsync(
            It.IsAny<PlayEntryData>(),
            It.IsAny<PlayContext>(),
            It.IsAny<CancellationToken>()))
            .Returns(() => Task.Delay(TimeSpan.FromSeconds(5)));
        using var db = new SqliteDatabaseManager(":memory:");
        var sessionManager = new SessionManager(db);
        var beatmapState = new Mock<IBeatmapStateTracker>();
        var gameState = new Mock<IGameStateManager>();
        gameState.Setup(g => g.IsReplay).Returns(false);
        var service = new PlaySubmissionService(sink.Object, sessionManager, beatmapState.Object, gameState.Object);
        service.TryPostBeatmapEntry(complete: true, totalBeatmapHits: 50);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => service.FlushPendingSubmissionsAsync(cts.Token);

        await act.Should().NotThrowAsync();
    }
}
