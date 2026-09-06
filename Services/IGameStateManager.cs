using Circle_Tracker;

namespace Circle_Tracker.Services;

public interface IGameStateManager
{
    GameStatus GameState { get; set; }
    bool IsPlaying { get; }
    bool IsReplay { get; }
    string DetectedClient { get; set; }
    bool MemoryReadError { get; }
    string Username { get; set; }
    string GameStateLabel { get; }

    GameStatus ParseGameState(int stateNumber);
    bool IsSongSelectState(GameStatus gs);
    string DetectClient(TosuState state);
    bool DetectReplay(TosuState state, string username);
    bool DetectReplay(TosuState state);
    void UpdateFromState(TosuState state);
}
