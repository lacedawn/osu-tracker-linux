using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    class Tracker
    {
        private readonly IMainWindow _form;
        private readonly TosuClient _tosuClient;

        private static string FindFile(string relativePath)
        {
            string p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(p2)) return p2;
            return p1;
        }

        private static string SettingsFilePath => Path.Combine(AppContext.BaseDirectory, "user_settings.txt");
        private static string CredentialsFilePath => FindFile("credentials.json");
        private static string SoundFilePath => FindFile(Path.Combine("assets", "sectionpass.wav"));

        private static readonly List<(string, string)> DataRanges = new List<(string, string)>()
        {
            ("Date and Time", "play_date"),
            ("Beatmap",       "beatmap_string"),
            ("HD",            "HD"),
            ("HR",            "HR"),
            ("DT",            "DT"),
            ("BPM",           "bpm"),
            ("Aim",           "aim"),
            ("Speed",         "speed"),
            ("Stars",         "stars"),
            ("CS",            "CS"),
            ("AR",            "AR"),
            ("OD",            "OD"),
            ("Objects Hit",   "hits"),
            ("Acc",           "acc"),
            ("300s",          "num300s"),
            ("100s",          "num100s"),
            ("50s",           "num50s"),
            ("Miss",          "misses"),
            ("EZ",            "EZ"),
            ("HT",            "HT"),
            ("FL",            "FL"),
            ("Map Complete",  "complete"),
            ("Playcount",     "playcount"),
            ("Time (s)",      "time_seconds"),
        };

        public int IdleSeconds = 0;
        public int PlayingSeconds = 0;

        public string TosuHost { get; set; } = "127.0.0.1";
        public int TosuPort { get; set; } = 24050;
        public string DetectedClient { get; private set; } = "Unknown";

        private string _currentBeatmapChecksum = "";
        public int BeatmapID { get; set; }
        public int BeatmapSetID { get; set; }
        public string BeatmapString { get; set; } = "";
        public int BeatmapBpm { get; set; }

        public bool SubmitSoundEnabled { get; set; }

        public GameStatus GameState { get; private set; } = GameStatus.Menu;
        public bool IsPlaying => GameState == GameStatus.Playing;
        public bool IsReplay { get; private set; } = false;
        public bool MemoryReadError { get; set; } = false;

        public string Username { get; set; } = "";
        public int RawMods { get; set; } = 0;
        public bool Hidden { get; set; } = false;
        public bool Hardrock { get; set; } = false;
        public bool Doubletime { get; set; } = false;
        public bool EZ { get; set; } = false;
        public bool Halftime { get; set; } = false;
        public bool Flashlight { get; set; } = false;
        public bool Auto { get; set; } = false;

        public decimal BeatmapStars { get; private set; }
        public decimal BeatmapAim { get; private set; }
        public decimal BeatmapSpeed { get; private set; }
        public decimal BeatmapCs { get; private set; }
        public decimal BeatmapAr { get; private set; }
        public decimal BeatmapOd { get; private set; }
        public int Play300c { get; set; } = 0;
        public int Play100c { get; set; } = 0;
        public int Play50c { get; set; } = 0;
        public int PlayMissc { get; set; } = 0;
        public int TotalBeatmapHits { get; set; } = 0;
        public decimal Accuracy { get; set; } = 0;
        public int Time { get; set; } = 0;

        private int _firstHitObjectTime = 0;
        private float _lastClockRate = 1f;
        private int _currentGameMode = 0;

        private DateTime LastPostTime { get; set; }
        private int _tickLock = 0;
        private bool SpreadsheetTimezoneVerified { get; set; } = false;
        public bool SheetsApiReady { get; set; } = false;
        public bool UseAltFuncSeparator { get; set; } = false;
        public string SpreadsheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public int SheetRows { get; set; }
        private SheetsService? GoogleSheetsService;
        private Spreadsheet? UserSpreadsheet;
        private Sheet? RawDataSheet;

        public Tracker(IMainWindow form, TosuClient tosuClient)
        {
            _form = form;
            _tosuClient = tosuClient;
            LoadSettings();

            _tosuClient.Host = TosuHost;
            _tosuClient.Port = TosuPort;

            GameState = GameStatus.Menu;
            LastPostTime = DateTime.Now;

            if (!File.Exists(SettingsFilePath))
            {
                string welcomeMsg = "Welcome to circle tracker!\n\n" +
                    "This app connects to 'tosu' running alongside osu!.\n\n" +
                    "Works with both osu!stable (Wine) and osu!lazer.";
                _form.ShowMessage(welcomeMsg, "Welcome to Circle Tracker!");
            }
        }

        public void SaveSettings()
        {
            try
            {
                string[] lines =
                {
                    SpreadsheetId,
                    SheetName,
                    SubmitSoundEnabled ? "1" : "0",
                    SpreadsheetTimezoneVerified ? "1" : "0",
                    UseAltFuncSeparator ? "1" : "0",
                    Username,
                    TosuHost,
                    TosuPort.ToString()
                };
                File.WriteAllLines(SettingsFilePath, lines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save settings: {ex.Message}");
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

            if (!File.Exists(SettingsFilePath)) return;

            try
            {
                var lines = File.ReadAllLines(SettingsFilePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    switch (i)
                    {
                        case 0: SpreadsheetId = lines[0]; break;
                        case 1: SheetName = lines[1]; break;
                        case 2: SubmitSoundEnabled = lines[2] == "1"; break;
                        case 3: SpreadsheetTimezoneVerified = lines[3] == "1"; break;
                        case 4: UseAltFuncSeparator = lines[4] == "1"; break;
                        case 5: Username = lines[5]; break;
                        case 6: TosuHost = !string.IsNullOrWhiteSpace(lines[6]) ? lines[6] : "127.0.0.1"; break;
                        case 7: if (int.TryParse(lines[7], out int port) && port > 0) TosuPort = port; break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        public string GetModsString()
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
        private string getFunctionSeparator() => GetFunctionSeparator();

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

        private static bool DetectReplay(TosuState state)
        {
            // Do NOT check state.Settings.ReplayUIVisible because in osu!stable that is true by default.
            // Check if playing username does not match profile username
            string? playName = state.Play?.PlayerName;
            string? profileName = state.Profile?.Name;
            if (!string.IsNullOrWhiteSpace(playName) &&
                !string.IsNullOrWhiteSpace(profileName) &&
                !string.Equals(playName.Trim(), profileName.Trim(), StringComparison.OrdinalIgnoreCase))
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
                    if (BeatmapBpm == 0 && attr.Bpm > 0) BeatmapBpm = (int)Math.Round((double)attr.Bpm);
                    if (attr.ClockRate > 0)
                        _lastClockRate = (float)attr.ClockRate;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CircleTracker] Failed to update difficulty from PP API: {ex.Message}");
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
                    Console.WriteLine($"[CircleTracker] Transitioned from Playing to {newGameState}. Completed={beatmapCompleted}. Hits={TotalBeatmapHits}");
                    TryPostBeatmapEntryToGoogleSheets(beatmapCompleted);

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

                    if (newHits > TotalBeatmapHits && newHits - TotalBeatmapHits < 50)
                    {
                        Accuracy = newAcc;
                        Play300c = new300c;
                        Play100c = new100c;
                        Play50c = new50c;
                        TotalBeatmapHits = newHits;
                    }

                    // detect retry when song time rewinds
                    if (newSongTime < Time && Time > 0)
                    {
                        Console.WriteLine($"[CircleTracker] Retry detected (Time rewound: {newSongTime} < {Time}). Hits={TotalBeatmapHits}");
                        TryPostBeatmapEntryToGoogleSheets(false);
                        Play300c = 0;
                        Play100c = 0;
                        Play50c = 0;
                        PlayMissc = 0;
                        TotalBeatmapHits = 0;
                        Time = 0;
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

        public void InitGoogleAPI(bool silent = false)
        {
            bool credentialsFound = File.Exists(CredentialsFilePath);
            _form.SetCredentialsFound(credentialsFound);
            if (!credentialsFound)
            {
                if (!silent) _form.ShowMessage($"credentials.json not found at {CredentialsFilePath}");
                SetSheetsApiReady(false);
                return;
            }
            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                if (!silent) _form.ShowMessage("Please enter a spreadsheet ID.");
                SetSheetsApiReady(false);
                return;
            }
            if (string.IsNullOrEmpty(SheetName))
            {
                if (!silent) _form.ShowMessage("Please enter a sheet name.");
                SetSheetsApiReady(false);
                return;
            }

            string[] Scopes = { SheetsService.Scope.Spreadsheets };
            GoogleCredential credential;
            try
            {
                using (var stream = new FileStream(CredentialsFilePath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
                }

                GoogleSheetsService = new SheetsService(new Google.Apis.Services.BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Circle Tracker"
                });

                var getSheetRequest = GoogleSheetsService.Spreadsheets.Get(SpreadsheetId);
                UserSpreadsheet = getSheetRequest.Execute();
            }
            catch (GoogleApiException e)
            {
                Console.Error.WriteLine($"[CircleTracker] Google API Exception in InitGoogleAPI: {e.Message}");
                if (!silent) _form.ShowMessage(e.Message, "Google Sheets API Error");
                SetSheetsApiReady(false);
                return;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[CircleTracker] Exception in InitGoogleAPI: {e.Message}");
                if (!silent) _form.ShowMessage(e.Message, "Error");
                SetSheetsApiReady(false);
                return;
            }

            try
            {
                RawDataSheet = UserSpreadsheet.Sheets.First(s => s.Properties.Title == SheetName);
            }
            catch
            {
                if (!silent) _form.ShowMessage($"No sheet named \"{SheetName}\" found.", "Error");
                SetSheetsApiReady(false);
                return;
            }
            SheetRows = RawDataSheet.Properties.GridProperties.RowCount ?? 1000;

            try { WriteHeaders(); }
            catch (GoogleApiException e)
            {
                if (!silent)
                {
                    _form.ShowMessage(e.Message, "Google Sheets API Error");
                    if (e.Message.Contains("Unable to parse range"))
                        _form.ShowMessage("Check that the Sheet Name matches an actual tab in your spreadsheet.");
                    if (e.Message.Contains("Requested entity was not found"))
                        _form.ShowMessage("Check that the Spreadsheet ID is correct.");
                }
                SetSheetsApiReady(false);
                return;
            }

            string range = $"'{SheetName}'!W2";
            var valueRange = new ValueRange();
            valueRange.Values = new List<IList<object>>
            {
                new List<object>
                {
                    $"=ARRAYFORMULA(IF(ISBLANK(hits) = false{getFunctionSeparator()} hits^0{getFunctionSeparator()}))"
                }
            };
            var writeRequest = GoogleSheetsService.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
            writeRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            try { writeRequest.Execute(); }
            catch (GoogleApiException e)
            {
                if (!silent) _form.ShowMessage(e.Message, $"Google Sheets API Error: Unable to Write Playcount to {range}");
                SetSheetsApiReady(false);
                return;
            }

            try { AddMissingNamedRanges(UserSpreadsheet, RawDataSheet); }
            catch (GoogleApiException e)
            {
                if (!silent) _form.ShowMessage(e.Message, "Google Sheets API Error: Unable to Add Named Ranges");
                SetSheetsApiReady(false);
                return;
            }

            ResizeNamedRanges(UserSpreadsheet, SheetRows);
            PromptTimezone(UserSpreadsheet);
            SetSheetsApiReady(true);
            Console.WriteLine("[CircleTracker] Google Sheets API successfully initialized and connected.");
        }

        private void WriteHeaders()
        {
            string range = $"'{SheetName}'!A1:X1";
            var valueRange = new ValueRange();
            var rawDataHeaders = DataRanges.Select(x => (object)x.Item1).ToList();
            valueRange.Values = new List<IList<object>> { rawDataHeaders };
            var writeRequest = GoogleSheetsService!.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
            writeRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            writeRequest.Execute();
        }

        private void PromptTimezone(Spreadsheet spreadsheet)
        {
            if (!SpreadsheetTimezoneVerified)
            {
                _ = Task.Run(async () =>
                {
                    bool confirmed = await _form.ShowYesNoDialog(
                        $"Your spreadsheet timezone is set to {spreadsheet.Properties.TimeZone}.\n\nIs this correct?",
                        "Confirm Timezone");
                    if (confirmed)
                    {
                        SpreadsheetTimezoneVerified = true;
                        SaveSettings();
                    }
                });
            }
        }

        private void AddMissingNamedRanges(Spreadsheet spreadsheet, Sheet rawDataSheet)
        {
            var namedRanges = DataRanges.Select(x => x.Item2).ToList();
            var existingRanges = spreadsheet.NamedRanges != null
                ? spreadsheet.NamedRanges.Select(nr => nr.Name).ToList()
                : new List<string>();

            var addRequests = new List<Request>();
            for (int i = 0; i < namedRanges.Count; i++)
            {
                if (!existingRanges.Contains(namedRanges[i]))
                {
                    var req = new Request();
                    req.AddNamedRange = new AddNamedRangeRequest();
                    req.AddNamedRange.NamedRange = new NamedRange();
                    req.AddNamedRange.NamedRange.Name = namedRanges[i];
                    req.AddNamedRange.NamedRange.Range = new GridRange
                    {
                        SheetId = rawDataSheet.Properties.SheetId,
                        StartColumnIndex = i,
                        EndColumnIndex = i + 1,
                        StartRowIndex = 1,
                        EndRowIndex = SheetRows
                    };
                    addRequests.Add(req);
                }
            }

            if (addRequests.Count > 0)
            {
                var reqs = new BatchUpdateSpreadsheetRequest { Requests = addRequests };
                GoogleSheetsService!.Spreadsheets.BatchUpdate(reqs, SpreadsheetId).Execute();
            }
        }

        private void ResizeNamedRanges(Spreadsheet spreadsheet, int rows)
        {
            var definedNames = DataRanges.Select(x => x.Item2).ToList();
            var rangesToUpdate = spreadsheet.NamedRanges != null
                ? spreadsheet.NamedRanges
                    .Where(nr => definedNames.Contains(nr.Name) && nr.Range.EndRowIndex != rows)
                    .ToList()
                : new List<NamedRange>();

            var rangeUpdateRequests = rangesToUpdate.Select(nr =>
            {
                var req = new Request();
                req.UpdateNamedRange = new UpdateNamedRangeRequest
                {
                    NamedRange = nr,
                    Fields = "Range"
                };
                req.UpdateNamedRange.NamedRange.Range.EndRowIndex = rows;
                return req;
            }).ToList();

            if (rangeUpdateRequests.Count > 0)
            {
                var reqs = new BatchUpdateSpreadsheetRequest { Requests = rangeUpdateRequests };
                GoogleSheetsService!.Spreadsheets.BatchUpdate(reqs, SpreadsheetId).Execute();
            }
        }

        public void TryPostBeatmapEntryToGoogleSheets(bool complete)
        {
            try
            {
                PostBeatmapEntryToGoogleSheets(complete);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CircleTracker] Exception in PostBeatmapEntryToGoogleSheets: {ex}");
            }
        }

        private void PostBeatmapEntryToGoogleSheets(bool complete)
        {
            Console.WriteLine($"[CircleTracker] Checking submission: Complete={complete}, Hits={TotalBeatmapHits}, Replay={IsReplay}, Mode={_currentGameMode}, SheetsReady={SheetsApiReady}");

            if (!SheetsApiReady)
            {
                Console.WriteLine("[CircleTracker] Skipped post: Sheets API not connected.");
                return;
            }
            if (IsReplay)
            {
                Console.WriteLine("[CircleTracker] Skipped post: Replay play detected.");
                return;
            }
            if (_currentGameMode != 0)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Game mode ({_currentGameMode}) is not osu!standard.");
                return;
            }
            var mods = (OsuMods)RawMods;
            if (mods.HasFlag(OsuMods.Autoplay) || mods.HasFlag(OsuMods.Relax) || mods.HasFlag(OsuMods.Autopilot))
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Disallowed mods active ({RawMods}).");
                return;
            }

            var timeSinceLastPost = DateTime.Now.Subtract(LastPostTime);
            if (timeSinceLastPost.TotalSeconds < 3)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Rate limited (<3s since last post).");
                return;
            }
            LastPostTime = DateTime.Now;

            if (TotalBeatmapHits < 40)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Total hits ({TotalBeatmapHits}) is below 40.");
                return;
            }

            decimal calculatedAccuracy =
                100 * (300M * Play300c + 100M * Play100c + 50M * Play50c)
                / (300M * (Play300c + Play100c + Play50c + PlayMissc));

            string dateTimeFormat = "yyyy'-'MM'-'dd h':'mm tt";
            string escapedName = (BeatmapString ?? "").Replace("\"", "\"\"");
            string modsString = GetModsString();
            if (modsString != "") modsString = $" +{modsString}";

            float clockRate = _lastClockRate > 0 ? _lastClockRate : (Doubletime ? 1.5f : Halftime ? 0.75f : 1f);
            int playTime = (int)(Math.Max(0, Time - _firstHitObjectTime) / clockRate / 1000f);

            var range = $"'{SheetName}'!A:X";
            var valueRange = new ValueRange();
            string sep = getFunctionSeparator();
            var writeData = new List<object>
            {
                /*A: Date & Time*/ DateTime.Now.ToString(dateTimeFormat, CultureInfo.InvariantCulture),
                /*B: Beatmap    */ $"=HYPERLINK(\"https://osu.ppy.sh/beatmapsets/{BeatmapSetID}#osu/{BeatmapID}\"{sep} \"{escapedName + modsString}\")",
                /*C: Hidden     */ Hidden     ? "1" : "",
                /*D: Hardrock   */ Hardrock   ? "1" : "",
                /*E: Doubletime */ Doubletime ? "1" : "",
                /*F: BPM        */ BeatmapBpm,
                /*G: Aim        */ BeatmapAim,
                /*H: Speed      */ BeatmapSpeed,
                /*I: Stars      */ BeatmapStars,
                /*J: CS         */ BeatmapCs,
                /*K: AR         */ BeatmapAr,
                /*L: OD         */ BeatmapOd,
                /*M: Hits       */ TotalBeatmapHits,
                /*N: Acc        */ (Accuracy == 0 || Accuracy == 100) ? calculatedAccuracy : Accuracy,
                /*O: 300c       */ Play300c,
                /*P: 100c       */ Play100c,
                /*Q: 50c        */ Play50c,
                /*R: Missc      */ PlayMissc,
                /*S: EZ         */ EZ         ? "1" : "",
                /*T: HT         */ Halftime   ? "1" : "",
                /*U: FL         */ Flashlight ? "1" : "",
                /*V: complete   */ complete ? "1" : "0",
                /*W: playcount  */ "",
                /*X: time       */ playTime
            };
            valueRange.Values = new List<IList<object>> { writeData };

            Console.WriteLine($"[CircleTracker] Sending append request to Google Sheets ({range})...");
            var appendRequest = GoogleSheetsService!.Spreadsheets.Values.Append(valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            AppendValuesResponse? appendResponse = null;
            const int MAX_SUBMIT_ATTEMPTS = 4;
            for (int i = 0; i < MAX_SUBMIT_ATTEMPTS; i++)
            {
                try
                {
                    appendResponse = appendRequest.Execute();
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[CircleTracker] Submit attempt {i + 1} failed: {ex.Message}");
                    if (i == MAX_SUBMIT_ATTEMPTS - 1) throw;
                }
            }

            Console.WriteLine($"[CircleTracker] Play successfully logged to Google Sheets!");

            if (SubmitSoundEnabled)
            {
                Console.WriteLine($"[CircleTracker] Playing submission sound ({SoundFilePath})...");
                SoundHelper.PlaySound(SoundFilePath);
            }

            if (appendResponse != null)
            {
                int updatedRow = 0;
                foreach (Match m in new Regex(@"\d+").Matches(appendResponse.Updates.UpdatedRange))
                {
                    int parsed = int.Parse(m.Value);
                    if (parsed > updatedRow) updatedRow = parsed;
                }

                if (updatedRow > SheetRows)
                {
                    var req = new Request();
                    req.AppendDimension = new AppendDimensionRequest
                    {
                        Dimension = "ROWS",
                        SheetId = RawDataSheet!.Properties.SheetId,
                        Length = 100
                    };
                    var b1 = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { req } };
                    GoogleSheetsService.Spreadsheets.BatchUpdate(b1, SpreadsheetId).Execute();
                    ResizeNamedRanges(UserSpreadsheet!, updatedRow + 100);
                    SheetRows = updatedRow + 100;
                }
            }
        }

        private void SetSheetsApiReady(bool val)
        {
            SheetsApiReady = val;
            _form.SetSheetsApiReady(val);
        }
    }
}
