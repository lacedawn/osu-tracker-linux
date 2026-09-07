using Circle_Tracker;
using Circle_Tracker.Services;
using FluentAssertions;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class GameStateManagerTests
{
    [Theory]
    [InlineData(0, GameStatus.Menu)]
    [InlineData(1, GameStatus.Edit)]
    [InlineData(2, GameStatus.Playing)]
    [InlineData(5, GameStatus.SongSelect)]
    [InlineData(7, GameStatus.ResultsScreen)]
    [InlineData(11, GameStatus.MultiplayerRoom)]
    [InlineData(12, GameStatus.MultiplayerSongSelect)]
    [InlineData(-1, GameStatus.Unknown)]
    [InlineData(99, GameStatus.Unknown)]
    public void ParseGameState_ValidStateNumber_ReturnsCorrectEnum(int stateNumber, GameStatus expected)
    {
        var manager = new GameStateManager();

        var result = manager.ParseGameState(stateNumber);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsSongSelectState_SongSelect_ReturnsTrue()
    {
        var manager = new GameStateManager();

        var result = manager.IsSongSelectState(GameStatus.SongSelect);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsSongSelectState_Playing_ReturnsFalse()
    {
        var manager = new GameStateManager();

        var result = manager.IsSongSelectState(GameStatus.Playing);

        result.Should().BeFalse();
    }

    [Fact]
    public void DetectClient_StableVersion_ReturnsStable()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            Settings = new TosuSettings
            {
                Client = new TosuClientInfo { Version = "osu!stable-2024" }
            }
        };

        var result = manager.DetectClient(state);

        result.Should().Be("osu!stable");
    }

    [Fact]
    public void DetectClient_EmptyVersion_ReturnsLazer()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            Settings = new TosuSettings
            {
                Client = new TosuClientInfo { Version = "" }
            }
        };

        var result = manager.DetectClient(state);

        result.Should().Be("osu!lazer");
    }

    [Fact]
    public void DetectReplay_DifferentPlayerName_ReturnsTrue()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            Play = new TosuPlay { PlayerName = "SpectatedPlayer" },
            Profile = new TosuProfile { Name = "LocalPlayer" }
        };

        var result = manager.DetectReplay(state, "LocalPlayer");

        result.Should().BeTrue();
    }

    [Fact]
    public void DetectReplay_SamePlayerName_ReturnsFalse()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            Play = new TosuPlay { PlayerName = "LocalPlayer" },
            Profile = new TosuProfile { Name = "LocalPlayer" }
        };

        var result = manager.DetectReplay(state, "LocalPlayer");

        result.Should().BeFalse();
    }

    [Fact]
    public void DetectReplay_LazerReplayUIVisible_ReturnsTrue()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            Settings = new TosuSettings
            {
                Client = new TosuClientInfo { Version = "lazer" },
                ReplayUIVisible = true
            },
            Play = new TosuPlay { PlayerName = "LocalPlayer" },
            Profile = new TosuProfile { Name = "LocalPlayer" }
        };

        var result = manager.DetectReplay(state, "LocalPlayer");

        result.Should().BeTrue();
    }

    [Fact]
    public void UpdateFromState_ValidPlayingState_UpdatesAllProperties()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Profile = new TosuProfile { Name = "PlayerOne" },
            Settings = new TosuSettings
            {
                Client = new TosuClientInfo { Version = "stable_2024" }
            },
            Play = new TosuPlay { PlayerName = "PlayerOne" }
        };

        manager.UpdateFromState(state);

        manager.GameState.Should().Be(GameStatus.Playing);
        manager.IsPlaying.Should().BeTrue();
        manager.IsReplay.Should().BeFalse();
        manager.DetectedClient.Should().Be("osu!stable");
        manager.Username.Should().Be("PlayerOne");
        manager.MemoryReadError.Should().BeFalse();
        manager.GameStateLabel.Should().Be("PLAYING");
    }

    [Fact]
    public void UpdateFromState_SongSelectWithMissingBeatmap_SetsMemoryReadErrorTrue()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 5 },
            Files = new TosuFiles { Beatmap = "" }
        };

        manager.UpdateFromState(state);

        manager.MemoryReadError.Should().BeTrue();
    }

    [Fact]
    public void UpdateFromState_NullState_DoesNotThrow()
    {
        var manager = new GameStateManager();

        var act = () => manager.UpdateFromState(null!);

        act.Should().NotThrow();
    }

    [Fact]
    public void DetectReplay_EmptyProfileName_ReturnsTrueInPlayingState()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Play = new TosuPlay { PlayerName = "SomePlayer" },
            Profile = new TosuProfile { Name = "" }
        };

        var result = manager.DetectReplay(state, "");

        result.Should().BeTrue();
    }

    [Fact]
    public void DetectReplay_EmptyProfileName_ReturnsTrueForNullProfileName()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Play = new TosuPlay { PlayerName = "SomePlayer" },
            Profile = new TosuProfile { Name = null }
        };

        var result = manager.DetectReplay(state, "");

        result.Should().BeTrue();
    }

    [Fact]
    public void DetectReplay_PopulatedProfileName_MatchingPlayer_ReturnsFalse()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Play = new TosuPlay { PlayerName = "lacedawn" },
            Profile = new TosuProfile { Name = "lacedawn" }
        };

        var result = manager.DetectReplay(state, "lacedawn");

        result.Should().BeFalse();
    }

    [Fact]
    public void DetectReplay_PopulatedProfileName_DifferentPlayer_ReturnsTrue()
    {
        var manager = new GameStateManager();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Play = new TosuPlay { PlayerName = "someone_else" },
            Profile = new TosuProfile { Name = "lacedawn" }
        };

        var result = manager.DetectReplay(state, "lacedawn");

        result.Should().BeTrue();
    }

    [Fact]
    public void TrackerSnapshot_EmptyProfile_ProfileIdentityConfirmedIsFalse()
    {
        var (tracker, client, _) = TrackerFactory.Create();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Profile = new TosuProfile { Name = "" },
            Play = new TosuPlay { PlayerName = "SomePlayer" }
        };
        client.Setup(c => c.LatestState).Returns(state);

        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        snapshot.ProfileIdentityConfirmed.Should().BeFalse();
    }

    [Fact]
    public void TrackerSnapshot_PopulatedProfile_ProfileIdentityConfirmedIsTrue()
    {
        var (tracker, client, _) = TrackerFactory.Create();
        var state = new TosuState
        {
            State = new TosuGameState { Number = 2 },
            Profile = new TosuProfile { Name = "lacedawn" },
            Play = new TosuPlay { PlayerName = "lacedawn" }
        };
        client.Setup(c => c.LatestState).Returns(state);

        tracker.Tick();
        var snapshot = tracker.GetSnapshot();

        snapshot.ProfileIdentityConfirmed.Should().BeTrue();
    }
}
