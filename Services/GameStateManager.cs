using Microsoft.Extensions.Logging;
using System;

namespace Circle_Tracker.Services;

public class GameStateManager : IGameStateManager
{
    private readonly ILogger<GameStateManager> _log;

    public GameStateManager(ILogger<GameStateManager>? log = null)
    {
        _log = log ?? AppLogger.For<GameStateManager>();
    }

    public GameStatus GameState { get; set; } = GameStatus.Menu;
    public bool IsPlaying => GameState == GameStatus.Playing;
    public bool IsReplay { get; private set; }
    public string DetectedClient { get; set; } = "Unknown";
    public bool MemoryReadError { get; private set; }
    public string Username { get; set; } = "";

    public string GameStateLabel => IsReplay
        ? "REPLAY"
        : GameState == GameStatus.Playing
            ? "PLAYING"
            : GameState == GameStatus.ResultsScreen
                ? "RESULTS"
                : "IDLE";

    public GameStatus ParseGameState(int stateNumber)
    {
        return stateNumber switch
        {
            0 => GameStatus.Menu,
            1 => GameStatus.Edit,
            2 => GameStatus.Playing,
            5 => GameStatus.SongSelect,
            7 => GameStatus.ResultsScreen,
            11 => GameStatus.MultiplayerRoom,
            12 => GameStatus.MultiplayerSongSelect,
            _ => GameStatus.Unknown
        };
    }

    public bool IsSongSelectState(GameStatus gs)
    {
        return gs == GameStatus.SongSelect
            || gs == GameStatus.MultiplayerRoom
            || gs == GameStatus.MultiplayerSongSelect;
    }

    public string DetectClient(TosuState state)
    {
        string ver = state?.Settings?.Client?.Version ?? "";
        if (string.IsNullOrEmpty(ver))
            return "osu!lazer";

        if (ver.Contains("cuttingedge", StringComparison.OrdinalIgnoreCase) ||
            ver.Contains("stable", StringComparison.OrdinalIgnoreCase) ||
            ver.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
            ver.StartsWith("b20", StringComparison.OrdinalIgnoreCase))
        {
            return "osu!stable";
        }

        return "osu!lazer";
    }

    public bool DetectReplay(TosuState state, string username)
    {
        if (state == null)
            return false;

        string? playName = state.Play?.PlayerName;
        string? profileName = !string.IsNullOrWhiteSpace(state.Profile?.Name) ? state.Profile.Name : username;

        bool isPlaying = (state.State != null ? ParseGameState(state.State.Number) : GameState) == GameStatus.Playing;
        if (string.IsNullOrWhiteSpace(profileName) && isPlaying)
        {
            _log.LogWarning("Profile name is empty while in Playing state. Replay detection may be unreliable. Treating as potential replay: {PlayerName}", state.Play?.PlayerName);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(playName) &&
            !string.IsNullOrWhiteSpace(profileName) &&
            !string.Equals(playName.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string client = !string.IsNullOrEmpty(DetectedClient) && DetectedClient != "Unknown"
            ? DetectedClient
            : DetectClient(state);

        if (client == "osu!lazer" && (state.Settings?.ReplayUIVisible ?? false))
        {
            return true;
        }

        return false;
    }

    public bool DetectReplay(TosuState state) => DetectReplay(state, Username);

    public void UpdateFromState(TosuState state)
    {
        if (state == null)
            return;

        DetectedClient = DetectClient(state);

        if (!string.IsNullOrEmpty(state.Profile?.Name))
        {
            Username = state.Profile.Name;
        }

        GameState = ParseGameState(state.State?.Number ?? -1);
        IsReplay = DetectReplay(state, Username);
        MemoryReadError = IsSongSelectState(GameState) && string.IsNullOrEmpty(state.Files?.Beatmap);
    }
}
