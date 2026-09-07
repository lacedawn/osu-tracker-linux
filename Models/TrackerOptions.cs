using Circle_Tracker.Services;
using Circle_Tracker.Storage;

namespace Circle_Tracker;

public record TrackerOptions(
    ITosuClient TosuClient,
    IPlaySink? PlaySink = null,
    SessionManager? SessionManager = null,
    ISheetsSink? SheetsSink = null,
    IGameStateManager? GameStateManager = null,
    IBeatmapStateTracker? BeatmapStateTracker = null,
    IPlaySubmissionService? PlaySubmissionService = null,
    ISettingsService? Settings = null
);
