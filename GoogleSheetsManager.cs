using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public record PlayEntryData(
        string BeatmapString, int BeatmapSetID, int BeatmapID,
        bool Hidden, bool Hardrock, bool Doubletime, bool EZ, bool Halftime, bool Flashlight,
        int BeatmapBpm, decimal BeatmapAim, decimal BeatmapSpeed, decimal BeatmapStars,
        decimal BeatmapCs, decimal BeatmapAr, decimal BeatmapOd,
        int TotalBeatmapHits, decimal Accuracy,
        int Play300c, int Play100c, int Play50c, int PlayMissc,
        bool Complete, int PlayTimeSeconds, string ModsString,
        int PlayCount
    );

    public class GoogleSheetsManager
    {
        private static string FindFile(string relativePath)
        {
            string p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(p2)) return p2;
            return p1;
        }

        public static string CredentialsFilePath => FindFile("credentials.json");

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

        private readonly IMainWindow _form;
        private readonly Func<string> _getFunctionSeparator;

        private SheetsService? _sheetsService;
        private Spreadsheet? _userSpreadsheet;
        private Sheet? _rawDataSheet;

        public bool SheetsApiReady { get; private set; } = false;
        public bool SpreadsheetTimezoneVerified { get; set; } = false;
        public string SpreadsheetId { get; set; } = "";
        public string SheetName { get; set; } = "";
        public bool UseAltFuncSeparator { get; set; } = false;
        public int SheetRows { get; private set; }

        public Action? OnSettingsChanged { get; set; }

        public GoogleSheetsManager(IMainWindow form, Func<string> getFunctionSeparator)
        {
            _form = form;
            _getFunctionSeparator = getFunctionSeparator;
        }

        private void SetSheetsApiReady(bool val)
        {
            SheetsApiReady = val;
            _form.SetSheetsApiReady(val);
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

                _sheetsService = new SheetsService(new Google.Apis.Services.BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Circle Tracker"
                });

                var getSheetRequest = _sheetsService.Spreadsheets.Get(SpreadsheetId);
                _userSpreadsheet = getSheetRequest.Execute();
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
                _rawDataSheet = _userSpreadsheet.Sheets.First(s => s.Properties.Title == SheetName);
            }
            catch
            {
                if (!silent) _form.ShowMessage($"No sheet named \"{SheetName}\" found.", "Error");
                SetSheetsApiReady(false);
                return;
            }
            SheetRows = _rawDataSheet.Properties.GridProperties.RowCount ?? 1000;

            try { WriteHeaders().GetAwaiter().GetResult(); }
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

            try { AddMissingNamedRanges(_userSpreadsheet, _rawDataSheet).GetAwaiter().GetResult(); }
            catch (GoogleApiException e)
            {
                if (!silent) _form.ShowMessage(e.Message, "Google Sheets API Error: Unable to Add Named Ranges");
                SetSheetsApiReady(false);
                return;
            }

            ResizeNamedRanges(_userSpreadsheet, SheetRows).GetAwaiter().GetResult();
            PromptTimezone(_userSpreadsheet);
            SetSheetsApiReady(true);
            Console.WriteLine("[CircleTracker] Google Sheets API successfully initialized and connected.");
        }

        private async Task WriteHeaders(CancellationToken ct = default)
        {
            char lastCol = (char)('A' + DataRanges.Count - 1);
            string range = $"'{SheetName}'!A1:{lastCol}1";
            var valueRange = new ValueRange();
            var rawDataHeaders = DataRanges.Select(x => (object)x.Item1).ToList();
            valueRange.Values = new List<IList<object>> { rawDataHeaders };
            var writeRequest = _sheetsService!.Spreadsheets.Values.Update(valueRange, SpreadsheetId, range);
            writeRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await writeRequest.ExecuteAsync(ct);
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
                        OnSettingsChanged?.Invoke();
                    }
                });
            }
        }

        private async Task AddMissingNamedRanges(Spreadsheet spreadsheet, Sheet rawDataSheet, CancellationToken ct = default)
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
                await _sheetsService!.Spreadsheets.BatchUpdate(reqs, SpreadsheetId).ExecuteAsync(ct);
            }
        }

        private async Task ResizeNamedRanges(Spreadsheet spreadsheet, int rows, CancellationToken ct = default)
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
                await _sheetsService!.Spreadsheets.BatchUpdate(reqs, SpreadsheetId).ExecuteAsync(ct);
            }
        }

        public async Task TryAppendPlayEntry(PlayEntryData data, bool isReplay, int rawMods, int currentGameMode, DateTime lastPostTime, Action<DateTime> setLastPostTime, string soundFilePath, bool submitSoundEnabled, CancellationToken ct = default)
        {
            try
            {
                await AppendPlayEntry(data, isReplay, rawMods, currentGameMode, lastPostTime, setLastPostTime, soundFilePath, submitSoundEnabled, ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CircleTracker] Exception in AppendPlayEntry: {ex}");
            }
        }

        private async Task AppendPlayEntry(PlayEntryData data, bool isReplay, int rawMods, int currentGameMode, DateTime lastPostTime, Action<DateTime> setLastPostTime, string soundFilePath, bool submitSoundEnabled, CancellationToken ct = default)
        {
            Console.WriteLine($"[CircleTracker] Checking submission: Complete={data.Complete}, Hits={data.TotalBeatmapHits}, Replay={isReplay}, Mode={currentGameMode}, SheetsReady={SheetsApiReady}");

            if (!SheetsApiReady)
            {
                Console.WriteLine("[CircleTracker] Skipped post: Sheets API not connected.");
                return;
            }
            if (isReplay)
            {
                Console.WriteLine("[CircleTracker] Skipped post: Replay play detected.");
                return;
            }
            if (currentGameMode != 0)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Game mode ({currentGameMode}) is not osu!standard.");
                return;
            }
            var mods = (OsuMods)rawMods;
            if (mods.HasFlag(OsuMods.Autoplay) || mods.HasFlag(OsuMods.Relax) || mods.HasFlag(OsuMods.Autopilot))
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Disallowed mods active ({rawMods}).");
                return;
            }

            var timeSinceLastPost = DateTime.Now.Subtract(lastPostTime);
            if (timeSinceLastPost.TotalSeconds < 3)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Rate limited (<3s since last post).");
                return;
            }
            setLastPostTime(DateTime.Now);

            if (data.TotalBeatmapHits < 40)
            {
                Console.WriteLine($"[CircleTracker] Skipped post: Total hits ({data.TotalBeatmapHits}) is below 40.");
                return;
            }

            decimal calculatedAccuracy =
                100 * (300M * data.Play300c + 100M * data.Play100c + 50M * data.Play50c)
                / (300M * (data.Play300c + data.Play100c + data.Play50c + data.PlayMissc));

            string dateTimeFormat = "yyyy'-'MM'-'dd h':'mm tt";
            string escapedName = (data.BeatmapString ?? "").Replace("\"", "\"\"");
            string modsString = data.ModsString;
            if (modsString != "") modsString = $" +{modsString}";

            string sep = _getFunctionSeparator();
            var range = $"'{SheetName}'!A:X";
            var valueRange = new ValueRange();
            var writeData = new List<object>
            {
                DateTime.Now.ToString(dateTimeFormat, CultureInfo.InvariantCulture),
                $"=HYPERLINK(\"https://osu.ppy.sh/beatmapsets/{data.BeatmapSetID}#osu/{data.BeatmapID}\"{sep} \"{escapedName + modsString}\")",
                data.Hidden     ? "1" : "",
                data.Hardrock   ? "1" : "",
                data.Doubletime ? "1" : "",
                data.BeatmapBpm,
                data.BeatmapAim,
                data.BeatmapSpeed,
                data.BeatmapStars,
                data.BeatmapCs,
                data.BeatmapAr,
                data.BeatmapOd,
                data.TotalBeatmapHits,
                (data.Accuracy == 0 || data.Accuracy == 100) ? calculatedAccuracy : data.Accuracy,
                data.Play300c,
                data.Play100c,
                data.Play50c,
                data.PlayMissc,
                data.EZ         ? "1" : "",
                data.Halftime   ? "1" : "",
                data.Flashlight ? "1" : "",
                data.Complete ? "1" : "0",
                data.PlayCount,
                data.PlayTimeSeconds
            };
            valueRange.Values = new List<IList<object>> { writeData };

            Console.WriteLine($"[CircleTracker] Sending append request to Google Sheets ({range})...");
            var appendRequest = _sheetsService!.Spreadsheets.Values.Append(valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

            AppendValuesResponse? appendResponse = null;
            const int MaxSubmitAttempts = 4;
            const int BaseRetryDelayMs = 500;
            for (int i = 0; i < MaxSubmitAttempts; i++)
            {
                try
                {
                    appendResponse = await appendRequest.ExecuteAsync(ct);
                    break;
                }
                catch (GoogleApiException ex) when (
                    (int)ex.HttpStatusCode == 429 || (int)ex.HttpStatusCode == 503)
                {
                    Console.Error.WriteLine($"[CircleTracker] Transient error on attempt {i + 1}: {ex.HttpStatusCode}");
                    if (i == MaxSubmitAttempts - 1) throw;
                    await Task.Delay(BaseRetryDelayMs * (1 << i), ct);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[CircleTracker] Submit attempt {i + 1} failed: {ex.Message}");
                    if (i == MaxSubmitAttempts - 1) throw;
                }
            }

            Console.WriteLine($"[CircleTracker] Play successfully logged to Google Sheets!");

            if (submitSoundEnabled)
            {
                Console.WriteLine($"[CircleTracker] Playing submission sound ({soundFilePath})...");
                SoundHelper.PlaySound(soundFilePath);
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
                        SheetId = _rawDataSheet!.Properties.SheetId,
                        Length = 100
                    };
                    var b1 = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { req } };
                    await _sheetsService.Spreadsheets.BatchUpdate(b1, SpreadsheetId).ExecuteAsync(ct);
                    await ResizeNamedRanges(_userSpreadsheet!, updatedRow + 100, ct);
                    SheetRows = updatedRow + 100;
                }
            }
        }
    }
}
