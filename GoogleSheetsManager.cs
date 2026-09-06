using Circle_Tracker.Services;
using Circle_Tracker.Storage;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
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
        int PlayCount,
        bool AccuracyReliable,
        string BeatmapTitle = "",
        string BeatmapArtist = "",
        string BeatmapVersion = "",
        decimal BeatmapHp = 0m,
        string BeatmapChecksum = ""
    )
    {
        private readonly int? _totalHits;

        public int TotalHits
        {
            get => _totalHits ?? ((Play300c + Play100c + Play50c + PlayMissc) > 0 ? (Play300c + Play100c + Play50c + PlayMissc) : TotalBeatmapHits);
            init => _totalHits = value;
        }
    }

    public class GoogleSheetsManager : ISheetsSink, IPlaySink
    {
        private static readonly ILogger<GoogleSheetsManager> _log = AppLogger.For<GoogleSheetsManager>();

        public string SinkName => "Google Sheets";
        public bool IsReady => SheetsApiReady;
        private DateTime _lastPostTime = DateTime.MinValue;

        private const int MinHitsToSubmit = 40;
        private const int RateLimitSeconds = 3;
        private const int MaxSubmitAttempts = 4;
        private const int BaseRetryDelayMs = 500;
        private const int RowExpansionBatchSize = 100;

        private static string FindFile(string relativePath)
        {
            string p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(p1)) return p1;
            string p2 = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(p2)) return p2;
            return p1;
        }

        public static string CredentialsFilePath => FindFile("credentials.json");

        public static SheetsService? CreateSheetsService()
        {
            try
            {
                string credPath = CredentialsFilePath;
                if (!File.Exists(credPath))
                {
                    _log.LogWarning("credentials.json not found at {Path}", credPath);
                    return null;
                }

                string[] scopes = { SheetsService.Scope.Spreadsheets };
                GoogleCredential credential;
                
                using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream).CreateScoped(scopes);
                }

                return new SheetsService(new Google.Apis.Services.BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Circle Tracker"
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to create Google Sheets service");
                return null;
            }
        }

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
        private readonly CircuitBreaker _circuitBreaker = new(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        private SheetsService? _sheetsService;
        private Spreadsheet? _userSpreadsheet;
        private Sheet? _rawDataSheet;

        public bool SheetsApiReady { get; internal set; } = false;
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

        public async Task InitGoogleAPIAsync(bool silent = false)
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
                _userSpreadsheet = await getSheetRequest.ExecuteAsync();
            }
            catch (GoogleApiException e)
            {
                _log.LogError(e, "Google API Exception in InitGoogleAPI");
                if (!silent) _form.ShowMessage(e.Message, "Google Sheets API Error");
                SetSheetsApiReady(false);
                return;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Exception in InitGoogleAPI");
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

            try { await WriteHeaders(); }
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

            try { await AddMissingNamedRanges(_userSpreadsheet, _rawDataSheet); }
            catch (GoogleApiException e)
            {
                if (!silent) _form.ShowMessage(e.Message, "Google Sheets API Error: Unable to Add Named Ranges");
                SetSheetsApiReady(false);
                return;
            }

            await ResizeNamedRanges(_userSpreadsheet, SheetRows);
            PromptTimezone(_userSpreadsheet);
            SetSheetsApiReady(true);
            _log.LogInformation("Google Sheets API successfully initialized and connected");
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

        private void PromptTimezone(Spreadsheet spreadsheet, CancellationToken ct = default)
        {
            if (!SpreadsheetTimezoneVerified)
            {
                _ = Task.Run(async () =>
                {
                    if (ct.IsCancellationRequested) return;
                    bool confirmed = await _form.ShowYesNoDialog(
                        $"Your spreadsheet timezone is set to {spreadsheet.Properties.TimeZone}.\n\nIs this correct?",
                        "Confirm Timezone");
                    if (confirmed)
                    {
                        SpreadsheetTimezoneVerified = true;
                        OnSettingsChanged?.Invoke();
                    }
                }, ct);
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

        public Task InitializeAsync(bool silent = false, CancellationToken ct = default)
        {
            return InitGoogleAPIAsync(silent);
        }

        public Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        {
            return TryAppendPlayEntry(data, context.IsReplay, context.RawMods, context.CurrentGameMode,
                _lastPostTime, t => _lastPostTime = t, context.SoundFilePath, context.SubmitSoundEnabled, ct);
        }

        public async Task TryAppendPlayEntry(PlayEntryData data, bool isReplay, int rawMods, int currentGameMode, DateTime lastPostTime, Action<DateTime> setLastPostTime, string? soundFilePath, bool submitSoundEnabled, CancellationToken ct = default)
        {
            try
            {
                await AppendPlayEntry(data, isReplay, rawMods, currentGameMode, lastPostTime, setLastPostTime, soundFilePath, submitSoundEnabled, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exception in AppendPlayEntry");
            }
        }

        internal string? GetSkipReason(PlayEntryData data, bool isReplay, int rawMods,
            int currentGameMode, DateTime lastPostTime)
        {
            if (!SheetsApiReady) return "Sheets API not connected";
            if (_circuitBreaker.CurrentState == CircuitState.Open)
                return "Circuit breaker open (Google Sheets temporarily unavailable)";
            if (isReplay) return "Replay detected";
            if (currentGameMode != 0) return $"Non-standard game mode ({currentGameMode})";
            var mods = (OsuMods)rawMods;
            if (mods.HasFlag(OsuMods.Autoplay) || mods.HasFlag(OsuMods.Relax) || mods.HasFlag(OsuMods.Autopilot))
                return $"Disallowed mods ({rawMods})";
            if ((DateTime.Now - lastPostTime).TotalSeconds < RateLimitSeconds)
                return $"Rate limited (<{RateLimitSeconds}s since last post)";
            if (data.TotalBeatmapHits < MinHitsToSubmit)
                return $"Hit count below minimum ({data.TotalBeatmapHits} < {MinHitsToSubmit})";
            return null;
        }

        internal List<object> BuildRowData(PlayEntryData data)
        {
            decimal calculatedAccuracy =
                (data.Play300c + data.Play100c + data.Play50c + data.PlayMissc) > 0
                ? 100M * (300M * data.Play300c + 100M * data.Play100c + 50M * data.Play50c)
                  / (300M * (data.Play300c + data.Play100c + data.Play50c + data.PlayMissc))
                : 0M;
            decimal accuracy = data.AccuracyReliable ? data.Accuracy : calculatedAccuracy;
            string dateTimeFormat = "yyyy'-'MM'-'dd h':'mm tt";
            string escapedName = (data.BeatmapString ?? "").Replace("\"", "\"\"");
            string modsLabel = data.ModsString.Length > 0 ? $" +{data.ModsString}" : "";
            string sep = _getFunctionSeparator();
            return new List<object>
            {
                DateTime.Now.ToString(dateTimeFormat, CultureInfo.InvariantCulture),
                $"=HYPERLINK(\"https://osu.ppy.sh/beatmapsets/{data.BeatmapSetID}#osu/{data.BeatmapID}\"{sep} \"{escapedName + modsLabel}\")",
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
                accuracy,
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
        }

        private async Task<AppendValuesResponse> SubmitRowAsync(List<object> rowData, CancellationToken ct)
        {
            if (!_circuitBreaker.AllowRequest())
            {
                _log.LogWarning("Circuit breaker prevented Google Sheets submission (state: {State})", _circuitBreaker.CurrentState);
                throw new InvalidOperationException("Circuit breaker is open");
            }

            string range = $"'{SheetName}'!A:X";
            var valueRange = new ValueRange { Values = new List<IList<object>> { rowData } };
            var appendRequest = _sheetsService!.Spreadsheets.Values.Append(valueRange, SpreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            _log.LogInformation("Appending row to Google Sheets ({Range})...", range);

            bool allAttemptsFailed = true;
            for (int i = 0; i < MaxSubmitAttempts; i++)
            {
                try
                {
                    var response = await appendRequest.ExecuteAsync(ct);
                    _circuitBreaker.RecordSuccess();
                    return response;
                }
                catch (GoogleApiException ex) when ((int)ex.HttpStatusCode is 429 or 503)
                {
                    if (i == MaxSubmitAttempts - 1)
                    {
                        _circuitBreaker.RecordFailure();
                        throw;
                    }
                    int delayMs = BaseRetryDelayMs * (1 << i);
                    _log.LogWarning("Transient error ({StatusCode}), retrying in {DelayMs}ms...", ex.HttpStatusCode, delayMs);
                    await Task.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    if (i == MaxSubmitAttempts - 1)
                    {
                        _circuitBreaker.RecordFailure();
                        throw;
                    }
                    _log.LogError(ex, "Submit attempt {Attempt} failed", i + 1);
                }
            }

            _circuitBreaker.RecordFailure();
            throw new InvalidOperationException("Unreachable");
        }

        private async Task ExpandSheetIfNeededAsync(AppendValuesResponse response, CancellationToken ct)
        {
            string updatedRange = response.Updates?.UpdatedRange ?? "";
            int bangIndex = updatedRange.IndexOf('!');
            if (bangIndex < 0) return;
            string endCell = updatedRange.Substring(bangIndex + 1);
            if (endCell.Contains(':'))
                endCell = endCell.Substring(endCell.IndexOf(':') + 1);
            string rowStr = new string(endCell.SkipWhile(char.IsLetter).ToArray());
            if (!int.TryParse(rowStr, out int updatedRow)) return;
            if (updatedRow > SheetRows)
            {
                var req = new Request
                {
                    AppendDimension = new AppendDimensionRequest
                    {
                        Dimension = "ROWS",
                        SheetId = _rawDataSheet!.Properties.SheetId,
                        Length = RowExpansionBatchSize
                    }
                };
                var batch = new BatchUpdateSpreadsheetRequest { Requests = new List<Request> { req } };
                await _sheetsService!.Spreadsheets.BatchUpdate(batch, SpreadsheetId).ExecuteAsync(ct);
                await ResizeNamedRanges(_userSpreadsheet!, updatedRow + RowExpansionBatchSize, ct);
                SheetRows = updatedRow + RowExpansionBatchSize;
                _log.LogInformation("Sheet expanded to {SheetRows} rows", SheetRows);
            }
        }

        private async Task AppendPlayEntry(PlayEntryData data, bool isReplay, int rawMods, int currentGameMode,
            DateTime lastPostTime, Action<DateTime> setLastPostTime, string? soundFilePath,
            bool submitSoundEnabled, CancellationToken ct)
        {
            string? skipReason = GetSkipReason(data, isReplay, rawMods, currentGameMode, lastPostTime);
            if (skipReason != null)
            {
                _log.LogInformation("Skipped post: {SkipReason}", skipReason);
                return;
            }
            setLastPostTime(DateTime.Now);
            List<object> rowData = BuildRowData(data);
            AppendValuesResponse response = await SubmitRowAsync(rowData, ct);
            _log.LogInformation("Play successfully logged to Google Sheets!");
            if (submitSoundEnabled && !string.IsNullOrEmpty(soundFilePath))
                SoundHelper.PlaySound(soundFilePath);
            await ExpandSheetIfNeededAsync(response, ct);
        }
    }
}
