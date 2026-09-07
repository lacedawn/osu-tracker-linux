using Circle_Tracker;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace CircleTracker.Tests
{
    internal static class StateBuilder
    {
        internal static TosuState Playing(
            int h300 = 0, int h100 = 0, int h50 = 0, int misses = 0,
            int songTimeMs = 30000, decimal accuracy = 0,
            string playerName = "testplayer", string profileName = "testplayer",
            string checksum = "abc123", int mods = 0,
            string title = "Title", string artist = "Artist", string version = "Hard",
            int beatmapId = 1, int beatmapSetId = 1, decimal hp = 6m)
            => Build(2, h300, h100, h50, misses, songTimeMs, accuracy, playerName, profileName, checksum, mods,
                     title, artist, version, beatmapId, beatmapSetId, hp);

        internal static TosuState Results(
            int h300 = 45, string checksum = "abc123",
            string title = "Title", string artist = "Artist", string version = "Hard",
            int beatmapId = 1, int beatmapSetId = 1, decimal hp = 6m)
            => Build(7, h300, 0, 0, 0, 30000, 100m, "testplayer", "testplayer", checksum, 0,
                     title, artist, version, beatmapId, beatmapSetId, hp);

        internal static TosuState Build(
            int gameStateNumber,
            int h300 = 0, int h100 = 0, int h50 = 0, int misses = 0,
            int songTimeMs = 30000, decimal accuracy = 0,
            string playerName = "testplayer", string profileName = "testplayer",
            string checksum = "abc123", int mods = 0,
            string title = "Title", string artist = "Artist", string version = "Hard",
            int beatmapId = 1, int beatmapSetId = 1, decimal hp = 6m)
        {
            return new TosuState
            {
                State = new TosuGameState { Number = gameStateNumber },
                Profile = new TosuProfile { Name = profileName },
                Beatmap = new TosuBeatmap
                {
                    Checksum = checksum,
                    Id = beatmapId,
                    Set = beatmapSetId,
                    Artist = artist,
                    Title = title,
                    Version = version,
                    Time = new TosuBeatmapTime { Live = songTimeMs, FirstObject = 0 },
                    Stats = new TosuBeatmapStats
                    {
                        Stars = new TosuStars { Total = 5 },
                        Hp = new TosuStatValue { Original = hp, Converted = hp }
                    }
                },
                Play = new TosuPlay
                {
                    PlayerName = playerName,
                    Accuracy = accuracy,
                    Mods = new TosuPlayMods { Number = mods },
                    Hits = new TosuHits { H300 = h300, H100 = h100, H50 = h50, Misses = misses },
                    Mode = new TosuNumberName { Number = 0 }
                },
                Settings = new TosuSettings
                {
                    Client = new TosuClientInfo { Version = "b20240101" },
                    Mode = new TosuNumberName { Number = 0 }
                }
            };
        }

        internal static TosuState WarmUpPlaying(string checksum = "abc123", int mods = 0)
            => Build(2, h300: 0, h100: 0, h50: 0, misses: 0, songTimeMs: 0, accuracy: 0,
                     checksum: checksum, mods: mods);
    }

    internal static class TrackerFactory
    {
        internal static (Tracker tracker, Mock<ITosuClient> client, Mock<Circle_Tracker.Storage.IPlaySink> sink)
            Create(bool connected = true)
        {
            var mockWindow = new Mock<IMainWindow>();
            mockWindow.Setup(w => w.ShowYesNoDialog(It.IsAny<string>(), It.IsAny<string>()))
                      .ReturnsAsync(true);

            var mockClient = new Mock<ITosuClient>();
            mockClient.Setup(c => c.IsConnected).Returns(connected);
            mockClient.Setup(c => c.CalculatePpAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((PpCalcResult?)null);

            var mockSink = new Mock<Circle_Tracker.Storage.IPlaySink>();
            mockSink.Setup(s => s.IsReady).Returns(true);
            mockSink.Setup(s => s.SinkName).Returns("Mock Sink");
            mockSink.Setup(s => s.InitializeAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            mockSink.Setup(s => s.TryLogPlayAsync(
                    It.IsAny<Circle_Tracker.PlayEntryData>(),
                    It.IsAny<Circle_Tracker.Storage.PlayContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var tracker = new Tracker(mockWindow.Object, mockClient.Object, mockSink.Object);
            return (tracker, mockClient, mockSink);
        }
    }
}
