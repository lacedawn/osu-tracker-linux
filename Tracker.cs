using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public enum GameStatus
    {
        Menu = 0,
        Edit = 1,
        Playing = 2,
        SongSelect = 5,
        ResultsScreen = 7,
        MultiplayerRoom = 11,
        MultiplayerSongSelect = 12,
        Unknown = -1
    }

    [Flags]
    public enum OsuMods
    {
        None = 0,
        NoFail = 1,
        Easy = 1 << 1,
        TouchDevice = 1 << 2,
        Hidden = 1 << 3,
        HardRock = 1 << 4,
        SuddenDeath = 1 << 5,
        DoubleTime = 1 << 6,
        Relax = 1 << 7,
        HalfTime = 1 << 8,
        Nightcore = 1 << 9,
        Flashlight = 1 << 10,
        Autoplay = 1 << 11,
        SpunOut = 1 << 12,
        Autopilot = 1 << 13,
        Perfect = 1 << 14,
    }

    public record TrackerSnapshot(
        bool IsPlaying,
        bool IsReplay,
        string DetectedClient,
        string BeatmapString,
        string BeatmapTitle,
        string BeatmapArtist,
        string BeatmapVersion,
        int BeatmapId,
        int BeatmapSetId,
        decimal BeatmapHp,
        decimal BeatmapStars,
        decimal BeatmapAim,
        decimal BeatmapSpeed,
        decimal BeatmapCs,
        decimal BeatmapAr,
        decimal BeatmapOd,
        int BeatmapBpm,
        int TotalBeatmapHits,
        int Play300c,
        int Play100c,
        int Play50c,
        int PlayMissc,
        decimal Accuracy,
        int Time,
        string ModsString,
        string GameStateLabel,
        bool SheetsApiReady,
        bool MemoryReadError,
        int PlayingSeconds,
        int IdleSeconds
    )
    {
        public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
    }

    class Tracker
    {
        private static readonly ILogger<Tracker> _log = AppLogger.For<Tracker>();

        private const int MinHitsToSubmit = 40;
        private const int MaxHitJumpPerTick = 50;

        private readonly IMainWindow _form;
        private readonly ITosuClient _tosuClient;
        private readonly ISheetsSink _sheetsManager;

        private static string FindFile(string relativePath)
        {
            string p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(p2)) return p2;
            return p1;
        }

        private static string SettingsFilePath => Path.Combine(AppContext.BaseDirectory, "user_settings.json");
        private static string SoundFilePath => FindFile(Path.Combine("assets", "sectionpass.wav"));

        public int IdleSeconds { get; private set; } = 0;
        public int PlayingSeconds { get; private set; } = 0;

        public string TosuHost { get; set; } = "127.0.0.1";
        public int TosuPort { get; set; } = 24050;
        public string DetectedClient { get; private set; } = "Unknown";

        private string _currentBeatmapChecksum = "";
        private int BeatmapID { get; set; }
        private int BeatmapSetID { get; set; }
        private string BeatmapString { get; set; } = "";
        private string _beatmapTitle = "";
        private string _beatmapArtist = "";
        private string _beatmapVersion = "";
        private decimal _beatmapHp;
        private int BeatmapBpm { get; set; }

        public bool SubmitSoundEnabled { get; set; }

        private GameStatus GameState { get; set; } = GameStatus.Menu;
        private bool IsPlaying => GameState == GameStatus.Playing;
        private bool IsReplay { get; set; } = false;
        private bool MemoryReadError { get; set; } = false;

        public string Username { get; set; } = "";
        private int RawMods { get; set; } = 0;
        private bool Hidden { get; set; } = false;
        private bool Hardrock { get; set; } = false;
        private bool Doubletime { get; set; } = false;
        private bool EZ { get; set; } = false;
        private bool Halftime { get; set; } = false;
        private bool Flashlight { get; set; } = false;
        private bool Auto { get; set; } = false;

        private decimal BeatmapStars { get; set; }
        private decimal BeatmapAim { get; set; }
        private decimal BeatmapSpeed { get; set; }
        private decimal BeatmapCs { get; set; }
        private decimal BeatmapAr { get; set; }
        private decimal BeatmapOd { get; set; }
        private int Play300c { get; set; } = 0;
        private int Play100c { get; set; } = 0;
        private int Play50c { get; set; } = 0;
        private int PlayMissc { get; set; } = 0;
        private int TotalBeatmapHits { get; set; } = 0;
        private decimal Accuracy { get; set; } = 0;
        private int Time { get; set; } = 0;

        private int _firstHitObjectTime = 0;
        private float _lastClockRate = 1f;
        private int _currentGameMode = 0;

        private DateTime LastPostTime { get; set; }
        private int _tickLock = 0;
        private readonly SemaphoreSlim _sheetsLock = new SemaphoreSlim(1, 1);
        private string _lastLoggedBeatmapChecksum = "";
        private int _lastLoggedBeatmapId = 0;
        private string _lastLoggedBeatmapString = "";
        private int _lastLoggedMods = -1;
        private int _consecutivePlayCount = 0;
        private bool _lastLoggedComplete = false;

        public bool SheetsApiReady => _sheetsManager.SheetsApiReady;
        public bool SpreadsheetTimezoneVerified
        {
            get => _sheetsManager.SpreadsheetTimezoneVerified;
            set => _sheetsManager.SpreadsheetTimezoneVerified = value;
        }
        public bool UseAltFuncSeparator
        {
            get => _sheetsManager.UseAltFuncSeparator;
            set => _sheetsManager.UseAltFuncSeparator = value;
        }
        public string SpreadsheetId
        {
            get => _sheetsManager.SpreadsheetId;
            set => _sheetsManager.SpreadsheetId = value;
        }
        public string SheetName
        {
            get => _sheetsManager.SheetName;
            set => _sheetsManager.SheetName = value;
        }
        public int SheetRows => _sheetsManager.SheetRows;

        public Tracker(IMainWindow form, ITosuClient tosuClient)
        {
            _form = form;
            _tosuClient = tosuClient;
            _sheetsManager = new GoogleSheetsManager(form, GetFunctionSeparator);
            _sheetsManager.OnSettingsChanged = SaveSettings;
            LoadSettings();

            _tosuClient.Host = TosuHost;
            _tosuClient.Port = TosuPort;

            GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;

            if (!File.Exists(SettingsFilePath) && !File.Exists(Path.Combine(AppContext.BaseDirectory, "user_settings.txt")))
            {
                string welcomeMsg = "Welcome to circle tracker!\n\n" +
                    "This app connects to 'tosu' running alongside osu!.\n\n" +
                    "Works with both osu!stable (Wine) and osu!lazer.";
                _form.ShowMessage(welcomeMsg, "Welcome to Circle Tracker!");
            }
        }

        public Tracker(IMainWindow form, ITosuClient tosuClient, ISheetsSink sheetsSink)
        {
            _form = form;
            _tosuClient = tosuClient;
            _sheetsManager = sheetsSink;
            _sheetsManager.OnSettingsChanged = SaveSettings;

            GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;
        }

        public void InitGoogleAPI(bool silent = false) => _sheetsManager.InitGoogleAPI(silent);

        public void SaveSettings()
        {
            try
            {
                var settings = new UserSettings
                {
                    SpreadsheetId = SpreadsheetId,
                    SheetName = SheetName,
                    SubmitSoundEnabled = SubmitSoundEnabled,
                    SpreadsheetTimezoneVerified = SpreadsheetTimezoneVerified,
                    UseAltFuncSeparator = UseAltFuncSeparator,
                    Username = Username,
                    TosuHost = TosuHost,
                    TosuPort = TosuPort
                };
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save settings");
            }
        }

        private void LoadSettings()
        {
            SpreadsheetId = "";
            SheetName = "Raw Data";
            SubmitSoundEnabled = true;
            SpreadsheetTimezoneVerified = false;
            UseAltFuncSeparator = false;
            Username = "";
            TosuHost = "127.0.0.1";
            TosuPort = 24050;
            if (!File.Exists(SettingsFilePath))
            {
                string oldPath = Path.Combine(AppContext.BaseDirectory, "user_settings.txt");
                if (File.Exists(oldPath))
                {
                    MigrateOldSettings(oldPath);
                }
                return;
            }
            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<UserSettings>(json);
                if (settings != null)
                {
                    SpreadsheetId = settings.SpreadsheetId;
                    SheetName = settings.SheetName;
                    SubmitSoundEnabled = settings.SubmitSoundEnabled;
                    SpreadsheetTimezoneVerified = settings.SpreadsheetTimezoneVerified;
                    UseAltFuncSeparator = settings.UseAltFuncSeparator;
                    Username = settings.Username;
                    TosuHost = !string.IsNullOrWhiteSpace(settings.TosuHost) ? settings.TosuHost : "127.0.0.1";
                    TosuPort = settings.TosuPort > 0 ? settings.TosuPort : 24050;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load settings");
            }
        }

        private void MigrateOldSettings(string oldPath)
        {
            try
            {
                var lines = File.ReadAllLines(oldPath);
                if (lines.Length > 0) SpreadsheetId = lines[0];
                if (lines.Length > 1) SheetName = lines[1];
                if (lines.Length > 2) SubmitSoundEnabled = lines[2] == "1";
                if (lines.Length > 3) SpreadsheetTimezoneVerified = lines[3] == "1";
                if (lines.Length > 4) UseAltFuncSeparator = lines[4] == "1";
                if (lines.Length > 5) Username = lines[5];
                if (lines.Length > 6 && !string.IsNullOrWhiteSpace(lines[6])) TosuHost = lines[6];
                if (lines.Length > 7 && int.TryParse(lines[7], out int port) && port > 0) TosuPort = port;
                SaveSettings();
                _log.LogInformation("Migrated settings from old text format to JSON");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to migrate old settings");
            }
        }

        public TrackerSnapshot GetSnapshot()
        {
            string gameStateLabel = IsReplay
                ? "REPLAY"
                : GameState == GameStatus.Playing
                    ? "PLAYING"
                    : GameState == GameStatus.ResultsScreen
                        ? "RESULTS"
                        : "IDLE";

            return new TrackerSnapshot(
                IsPlaying: IsPlaying,
                IsReplay: IsReplay,
                DetectedClient: DetectedClient,
                BeatmapString: BeatmapString ?? "",
                BeatmapTitle: _beatmapTitle,
                BeatmapArtist: _beatmapArtist,
                BeatmapVersion: _beatmapVersion,
                BeatmapId: BeatmapID,
                BeatmapSetId: BeatmapSetID,
                BeatmapHp: _beatmapHp,
                BeatmapStars: BeatmapStars,
                BeatmapAim: BeatmapAim,
                BeatmapSpeed: BeatmapSpeed,
                BeatmapCs: BeatmapCs,
                BeatmapAr: BeatmapAr,
                BeatmapOd: BeatmapOd,
                BeatmapBpm: BeatmapBpm,
                TotalBeatmapHits: TotalBeatmapHits,
                Play300c: Play300c,
                Play100c: Play100c,
                Play50c: Play50c,
                PlayMissc: PlayMissc,
                Accuracy: Accuracy,
                Time: Time,
                ModsString: GetModsString(),
                GameStateLabel: gameStateLabel,
                SheetsApiReady: SheetsApiReady,
                MemoryReadError: MemoryReadError,
                PlayingSeconds: PlayingSeconds,
                IdleSeconds: IdleSeconds
            );
        }

        private string GetModsString()
        {
            string mods = "";
            if (Auto) mods += "AT";
            if (EZ) mods += "EZ";
            if (Halftime) mods += "HT";
            if (Hidden) mods += "HD";
            if (Hardrock) mods += "HR";
            if (Doubletime) mods += "DT";
            if (Flashlight) mods += "FL";
            return mods;
        }

        public string GetFunctionSeparator() => UseAltFuncSeparator ? ";" : ",";

        private static string DetectClient(TosuState state)
        {
            string ver = state.Settings?.Client?.Version ?? "";
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

        private bool DetectReplay(TosuState state)
        {
            string? playName = state.Play?.PlayerName;
            string? profileName = !string.IsNullOrWhiteSpace(state.Profile?.Name) ? state.Profile.Name : Username;
            if (!string.IsNullOrWhiteSpace(playName) &&
                !string.IsNullOrWhiteSpace(profileName) &&
                !string.Equals(playName.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (DetectedClient == "osu!lazer" && (state.Settings?.ReplayUIVisible ?? false))
            {
                return true;
            }

            return false;
        }

        private static GameStatus ParseGameState(int stateNumber)
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

        private static bool IsSongSelectState(GameStatus gs)
        {
            return gs == GameStatus.SongSelect
                || gs == GameStatus.MultiplayerRoom
                || gs == GameStatus.MultiplayerSongSelect;
        }

        private void UpdateModsFromBitfield(int rawMods)
        {
            RawMods = rawMods;
            var mods = (OsuMods)rawMods;
            Hidden = mods.HasFlag(OsuMods.Hidden);
            Hardrock = mods.HasFlag(OsuMods.HardRock);
            Doubletime = mods.HasFlag(OsuMods.DoubleTime) || mods.HasFlag(OsuMods.Nightcore);
            EZ = mods.HasFlag(OsuMods.Easy);
            Halftime = mods.HasFlag(OsuMods.HalfTime);
            Flashlight = mods.HasFlag(OsuMods.Flashlight);
            Auto = mods.HasFlag(OsuMods.Autoplay);
        }

        private void UpdateBeatmapFromState(TosuState state)
        {
            var bm = state.Beatmap;
            if (bm == null) return;

            BeatmapID = bm.Id;
            BeatmapSetID = bm.Set;
            _beatmapTitle = bm.Title ?? "";
            _beatmapArtist = bm.Artist ?? "";
            _beatmapVersion = bm.Version ?? "";
            _beatmapHp = bm.Stats?.Hp?.Converted ?? bm.Stats?.Hp?.Original ?? 0;
            BeatmapString = $"{bm.Artist} - {bm.Title} [{bm.Version}]";
            BeatmapBpm = (int)Math.Round((double)(bm.Stats?.Bpm?.Common ?? 0));

            _firstHitObjectTime = bm.Time?.FirstObject ?? 0;

            decimal totalStars = bm.Stats?.Stars?.Total ?? 0;
            decimal liveStars = bm.Stats?.Stars?.Live ?? 0;
            BeatmapStars = totalStars > 0 ? totalStars : liveStars;
            BeatmapAim = bm.Stats?.Stars?.Aim ?? 0;
            BeatmapSpeed = bm.Stats?.Stars?.Speed ?? 0;

            BeatmapCs = bm.Stats?.Cs?.Converted ?? bm.Stats?.Cs?.Original ?? 0;
            BeatmapAr = bm.Stats?.Ar?.Converted ?? bm.Stats?.Ar?.Original ?? 0;
            BeatmapOd = bm.Stats?.Od?.Converted ?? bm.Stats?.Od?.Original ?? 0;
        }

        private async Task UpdateDifficultyFromPpApi(int modNumber)
        {
            try
            {
                var ppResult = await _tosuClient.CalculatePpAsync(modNumber);
                var diff = ppResult?.Difficulty ?? ppResult?.Performance?.Difficulty;
                if (diff != null)
                {
                    if (BeatmapAim == 0 && diff.Aim > 0) BeatmapAim = diff.Aim;
                    if (BeatmapSpeed == 0 && diff.Speed > 0) BeatmapSpeed = diff.Speed;
                    if (BeatmapStars == 0 && diff.Stars > 0) BeatmapStars = diff.Stars;
                    if (BeatmapAr == 0 && diff.Ar > 0) BeatmapAr = diff.Ar;
                    if (BeatmapOd == 0 && diff.Od > 0) BeatmapOd = diff.Od;
                    if (BeatmapCs == 0 && diff.Cs > 0) BeatmapCs = diff.Cs;
                    if (_beatmapHp == 0 && diff.Hp > 0) _beatmapHp = diff.Hp;
                    if (BeatmapBpm == 0 && diff.Bpm > 0) BeatmapBpm = (int)Math.Round((double)diff.Bpm);
                    if (diff.ClockRate > 0)
                        _lastClockRate = (float)diff.ClockRate;
                }
                if (ppResult?.Attributes != null)
                {
                    var attr = ppResult.Attributes;
                    if (BeatmapAr == 0 && attr.Ar > 0) BeatmapAr = attr.Ar;
                    if (BeatmapOd == 0 && attr.Od > 0) BeatmapOd = attr.Od;
                    if (BeatmapCs == 0 && attr.Cs > 0) BeatmapCs = attr.Cs;
                    if (_beatmapHp == 0 && attr.Hp > 0) _beatmapHp = attr.Hp;
                    if (BeatmapBpm == 0 && attr.Bpm > 0) BeatmapBpm = (int)Math.Round((double)attr.Bpm);
                    if (attr.ClockRate > 0)
                        _lastClockRate = (float)attr.ClockRate;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to update difficulty from PP API");
            }
        }

        public void Tick()
        {
            if (!_tosuClient.IsConnected)
            {
                DetectedClient = "Disconnected";
                return;
            }

            var state = _tosuClient.LatestState;
            if (state == null)
            {
                DetectedClient = "Connecting...";
                return;
            }

            DetectedClient = DetectClient(state);

            GameStatus newGameState = ParseGameState(state.State?.Number ?? -1);
            bool songSelectGameState = IsSongSelectState(newGameState);

            if (!string.IsNullOrEmpty(state.Profile?.Name))
                Username = state.Profile.Name;

            string newChecksum = state.Beatmap?.Checksum ?? "";
            if (newChecksum != _currentBeatmapChecksum && newChecksum != "")
            {
                _currentBeatmapChecksum = newChecksum;
                UpdateBeatmapFromState(state);
                _ = UpdateDifficultyFromPpApi(state.Play?.Mods?.Number ?? 0);
            }

            _currentGameMode = state.Play?.Mode?.Number ?? state.Settings?.Mode?.Number ?? 0;
            IsReplay = DetectReplay(state);

            MemoryReadError = songSelectGameState && string.IsNullOrEmpty(state.Files?.Beatmap);
            if (MemoryReadError && string.IsNullOrEmpty(BeatmapString))
                BeatmapString = "";

            if (state.Beatmap?.Stats?.Stars != null)
            {
                var stars = state.Beatmap.Stats.Stars;
                if (stars.Total > 0) BeatmapStars = stars.Total;
                if (stars.Aim > 0) BeatmapAim = stars.Aim;
                if (stars.Speed > 0) BeatmapSpeed = stars.Speed;
            }

            if (newGameState != GameState)
            {
                if (GameState == GameStatus.Playing && newGameState != GameStatus.Playing)
                {
                    bool beatmapCompleted = newGameState == GameStatus.ResultsScreen;
                    _log.LogInformation("Transitioned from Playing to {NewGameState}. Completed={Completed}. Hits={Hits}",
                        newGameState, beatmapCompleted, TotalBeatmapHits);
                    TryPostBeatmapEntry(beatmapCompleted);

                    Play300c = 0;
                    Play100c = 0;
                    Play50c = 0;
                    PlayMissc = 0;
                    Accuracy = 0;
                    TotalBeatmapHits = 0;
                    Time = 0;
                }
                GameState = newGameState;
            }

            if (songSelectGameState && state.Play?.Mods != null)
            {
                int newMods = state.Play.Mods.Number;
                if (newMods != RawMods)
                {
                    UpdateModsFromBitfield(newMods);
                    UpdateBeatmapFromState(state);
                    _ = UpdateDifficultyFromPpApi(newMods);
                }
            }

            if (newGameState == GameStatus.Playing && state.Play != null)
            {
                var hits = state.Play.Hits;
                if (hits != null)
                {
                    decimal newAcc = state.Play.Accuracy;
                    int new300c = hits.H300;
                    int new100c = hits.H100;
                    int new50c = hits.H50;
                    int newMissc = hits.Misses;
                    int newHits = new300c + new100c + new50c;
                    int newSongTime = state.Beatmap?.Time?.Live ?? 0;

                    if (newMissc > PlayMissc)
                        PlayMissc = newMissc;

                    if (newHits > TotalBeatmapHits && newHits - TotalBeatmapHits < MaxHitJumpPerTick)
                    {
                        Accuracy = newAcc;
                        Play300c = new300c;
                        Play100c = new100c;
                        Play50c = new50c;
                        TotalBeatmapHits = newHits;
                    }

                    if (newSongTime < Time && Time > 0)
                    {
                        if (TotalBeatmapHits >= MinHitsToSubmit)
                        {
                            _log.LogInformation("Retry detected (Time rewound: {NewSongTime} < {Time}). Hits={Hits}",
                                newSongTime, Time, TotalBeatmapHits);
                            TryPostBeatmapEntry(false);
                        }
                        Play300c = 0;
                        Play100c = 0;
                        Play50c = 0;
                        PlayMissc = 0;
                        Accuracy = 0;
                        TotalBeatmapHits = 0;
                        Time = newSongTime;
                    }
                    else
                    {
                        Time = newSongTime;
                    }
                }

                if (state.Play.Mods != null)
                    UpdateModsFromBitfield(state.Play.Mods.Number);
            }
        }

        public void TickWrapper()
        {
            if (Interlocked.CompareExchange(ref _tickLock, 1, 0) != 0)
                return;
            try
            {
                Tick();
            }
            finally
            {
                Interlocked.Exchange(ref _tickLock, 0);
            }
        }

        public void TickEverySecond()
        {
            if (IsPlaying)
                PlayingSeconds++;
            else
                IdleSeconds++;
            _form.UpdateTime();
        }

        private void TryPostBeatmapEntry(bool complete)
        {
            if (TotalBeatmapHits < MinHitsToSubmit || IsReplay || _currentGameMode != 0)
                return;

            bool isSameMap = (!string.IsNullOrEmpty(_currentBeatmapChecksum) && _currentBeatmapChecksum == _lastLoggedBeatmapChecksum)
                || (BeatmapID > 0 && BeatmapID == _lastLoggedBeatmapId)
                || (!string.IsNullOrEmpty(BeatmapString) && BeatmapString == _lastLoggedBeatmapString);

            if (isSameMap && RawMods == _lastLoggedMods && !_lastLoggedComplete)
            {
                _consecutivePlayCount++;
            }
            else
            {
                _consecutivePlayCount = 1;
                _lastLoggedBeatmapChecksum = _currentBeatmapChecksum;
                _lastLoggedBeatmapId = BeatmapID;
                _lastLoggedBeatmapString = BeatmapString;
                _lastLoggedMods = RawMods;
            }

            _lastLoggedComplete = complete;

            float clockRate = _lastClockRate > 0 ? _lastClockRate : (Doubletime ? 1.5f : Halftime ? 0.75f : 1f);
            int playTime = (int)(Math.Max(0, Time - _firstHitObjectTime) / clockRate / 1000f);
            bool accuracyReliable = Accuracy > 0 && TotalBeatmapHits > 0;

            var data = new PlayEntryData(
                BeatmapString: BeatmapString,
                BeatmapSetID: BeatmapSetID,
                BeatmapID: BeatmapID,
                Hidden: Hidden,
                Hardrock: Hardrock,
                Doubletime: Doubletime,
                EZ: EZ,
                Halftime: Halftime,
                Flashlight: Flashlight,
                BeatmapBpm: BeatmapBpm,
                BeatmapAim: BeatmapAim,
                BeatmapSpeed: BeatmapSpeed,
                BeatmapStars: BeatmapStars,
                BeatmapCs: BeatmapCs,
                BeatmapAr: BeatmapAr,
                BeatmapOd: BeatmapOd,
                TotalBeatmapHits: TotalBeatmapHits,
                Accuracy: Accuracy,
                Play300c: Play300c,
                Play100c: Play100c,
                Play50c: Play50c,
                PlayMissc: PlayMissc,
                Complete: complete,
                PlayTimeSeconds: playTime,
                ModsString: GetModsString(),
                PlayCount: _consecutivePlayCount,
                AccuracyReliable: accuracyReliable
            );

            _ = Task.Run(async () =>
            {
                await _sheetsLock.WaitAsync();
                try
                {
                    await _sheetsManager.TryAppendPlayEntry(data, isReplay: IsReplay, rawMods: RawMods,
                        currentGameMode: _currentGameMode, lastPostTime: LastPostTime,
                        setLastPostTime: t => LastPostTime = t,
                        soundFilePath: SoundFilePath, submitSoundEnabled: SubmitSoundEnabled);
                }
                finally { _sheetsLock.Release(); }
            });
        }
    }
}
