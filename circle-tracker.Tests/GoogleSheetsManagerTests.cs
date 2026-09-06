using Circle_Tracker;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace CircleTracker.Tests
{
    public class GoogleSheetsManagerTests
    {
        private static PlayEntryData MakeData(
            int hits = 50, decimal accuracy = 97.5m, bool accuracyReliable = true,
            int h300 = 45, int h100 = 5, int h50 = 0, int misses = 0,
            bool complete = true, int mods = 0, string modsString = "",
            bool hidden = false, bool hardrock = false, bool doubletime = false,
            bool ez = false, bool halftime = false, bool flashlight = false)
        {
            return new PlayEntryData(
                BeatmapString: "Artist - Title [Hard]",
                BeatmapSetID: 123,
                BeatmapID: 456,
                Hidden: hidden, Hardrock: hardrock, Doubletime: doubletime,
                EZ: ez, Halftime: halftime, Flashlight: flashlight,
                BeatmapBpm: 180, BeatmapAim: 2.5m, BeatmapSpeed: 2.1m, BeatmapStars: 5.0m,
                BeatmapCs: 4.0m, BeatmapAr: 9.0m, BeatmapOd: 8.5m,
                TotalBeatmapHits: hits, Accuracy: accuracy,
                Play300c: h300, Play100c: h100, Play50c: h50, PlayMissc: misses,
                Complete: complete, PlayTimeSeconds: 120,
                ModsString: modsString, PlayCount: 1,
                AccuracyReliable: accuracyReliable
            );
        }

        private static GoogleSheetsManager MakeManager(bool apiReady = true)
        {
            var mockWindow = new Mock<IMainWindow>();
            var manager = new GoogleSheetsManager(mockWindow.Object, () => ",");
            manager.SheetsApiReady = apiReady;
            return manager;
        }

        [Fact]
        public void GetSkipReason_WhenApiNotReady_ReturnsNotConnected()
        {
            var manager = MakeManager(apiReady: false);
            var data = MakeData();

            string? reason = manager.GetSkipReason(data, false, 0, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("not connected");
        }

        [Fact]
        public void GetSkipReason_WhenReplay_ReturnsReplayReason()
        {
            var manager = MakeManager();
            var data = MakeData();

            string? reason = manager.GetSkipReason(data, isReplay: true, 0, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("Replay");
        }

        [Fact]
        public void GetSkipReason_WhenNonStandardMode_ReturnsGameModeReason()
        {
            var manager = MakeManager();
            var data = MakeData();

            string? reason = manager.GetSkipReason(data, false, 0, currentGameMode: 1, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("game mode");
        }

        [Fact]
        public void GetSkipReason_WhenAutoplayMod_ReturnsDisallowedModsReason()
        {
            var manager = MakeManager();
            var data = MakeData();
            int autoplayMod = (int)OsuMods.Autoplay;

            string? reason = manager.GetSkipReason(data, false, autoplayMod, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("Disallowed mods");
        }

        [Fact]
        public void GetSkipReason_WhenRateLimited_ReturnsRateLimitReason()
        {
            var manager = MakeManager();
            var data = MakeData();

            string? reason = manager.GetSkipReason(data, false, 0, 0, DateTime.Now);

            reason.Should().Contain("Rate limited");
        }

        [Fact]
        public void GetSkipReason_WhenHitsBelowMinimum_ReturnsHitCountReason()
        {
            var manager = MakeManager();
            var data = MakeData(hits: 5, h300: 5);

            string? reason = manager.GetSkipReason(data, false, 0, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("below minimum");
        }

        [Fact]
        public void GetSkipReason_WhenAllConditionsMet_ReturnsNull()
        {
            var manager = MakeManager();
            var data = MakeData(hits: 50, h300: 50);

            string? reason = manager.GetSkipReason(data, false, 0, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().BeNull();
        }

        [Fact]
        public void BuildRowData_WhenAccuracyReliable_UsesApiAccuracy()
        {
            var manager = MakeManager();
            var data = MakeData(accuracy: 100.0m, accuracyReliable: true, h300: 50, h100: 0, h50: 0, misses: 0, hits: 50);

            List<object> row = manager.BuildRowData(data);

            row[13].Should().Be(100.0m);
        }

        [Fact]
        public void BuildRowData_WhenAccuracyNotReliable_UsesCalculatedAccuracy()
        {
            var manager = MakeManager();
            var data = MakeData(accuracy: 0m, accuracyReliable: false, h300: 50, h100: 0, h50: 0, misses: 0, hits: 50);

            List<object> row = manager.BuildRowData(data);

            row[13].Should().Be(100.0m);
        }

        [Fact]
        public void BuildRowData_WhenHiddenMod_SetsHdColumn()
        {
            var manager = MakeManager();
            var data = MakeData(hidden: true);

            List<object> row = manager.BuildRowData(data);

            row[2].Should().Be("1");
        }

        [Fact]
        public void BuildRowData_WhenNoMods_ModColumnsAreEmpty()
        {
            var manager = MakeManager();
            var data = MakeData();

            List<object> row = manager.BuildRowData(data);

            row[2].Should().Be("");
            row[3].Should().Be("");
            row[4].Should().Be("");
        }

        [Fact]
        public void BuildRowData_WhenComplete_SetsCompleteColumnToOne()
        {
            var manager = MakeManager();
            var data = MakeData(complete: true);

            List<object> row = manager.BuildRowData(data);

            row[21].Should().Be("1");
        }

        [Fact]
        public void BuildRowData_WhenIncomplete_SetsCompleteColumnToZero()
        {
            var manager = MakeManager();
            var data = MakeData(complete: false);

            List<object> row = manager.BuildRowData(data);

            row[21].Should().Be("0");
        }

        [Fact]
        public void BuildRowData_AccuracyCalculation_HandlesAllMisses()
        {
            var manager = MakeManager();
            var data = MakeData(accuracy: 0m, accuracyReliable: false,
                h300: 0, h100: 0, h50: 0, misses: 0, hits: 0);

            List<object> row = manager.BuildRowData(data);

            row[13].Should().Be(0m);
        }

        [Fact]
        public void GetSkipReason_WhenCircuitBreakerOpen_ReturnsCircuitBreakerReason()
        {
            var manager = MakeManager();
            var data = MakeData();
            var circuitBreakerField = typeof(GoogleSheetsManager)
                .GetField("_circuitBreaker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var circuitBreaker = circuitBreakerField!.GetValue(manager) as Circle_Tracker.Services.CircuitBreaker;

            circuitBreaker!.RecordFailure();
            circuitBreaker!.RecordFailure();
            circuitBreaker!.RecordFailure();

            string? reason = manager.GetSkipReason(data, false, 0, 0, DateTime.Now.AddSeconds(-10));

            reason.Should().Contain("Circuit breaker");
            reason.Should().Contain("temporarily unavailable");
        }
    }
}
