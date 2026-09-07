using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public class BeatmapStateTracker : IBeatmapStateTracker
{
    private static readonly ILogger<BeatmapStateTracker> _log = AppLogger.For<BeatmapStateTracker>();

    private readonly ITosuClient? _tosuClient;
    private CancellationTokenSource? _ppApiCts;
    private readonly object _difficultyLock = new();

    public string CurrentBeatmapChecksum { get; set; } = "";
    public int BeatmapID { get; set; }
    public int BeatmapSetID { get; set; }
    public string BeatmapString { get; set; } = "";
    public string BeatmapTitle { get; set; } = "";
    public string BeatmapArtist { get; set; } = "";
    public string BeatmapVersion { get; set; } = "";
    public decimal BeatmapHp { get; set; }
    public int BeatmapBpm { get; set; }
    public decimal BeatmapStars { get; set; }
    public decimal BeatmapAim { get; set; }
    public decimal BeatmapSpeed { get; set; }
    public decimal BeatmapCs { get; set; }
    public decimal BeatmapAr { get; set; }
    public decimal BeatmapOd { get; set; }
    public int FirstHitObjectTime { get; set; }
    public float LastClockRate { get; set; } = 1f;

    public int RawMods { get; set; }
    public bool Hidden { get; set; }
    public bool Hardrock { get; set; }
    public bool Doubletime { get; set; }
    public bool EZ { get; set; }
    public bool Halftime { get; set; }
    public bool Flashlight { get; set; }
    public bool Auto { get; set; }

    public BeatmapStateTracker(ITosuClient? tosuClient = null)
    {
        _tosuClient = tosuClient;
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

    public void UpdateModsFromBitfield(int rawMods)
    {
        lock (_difficultyLock)
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
    }

    public void UpdateBeatmapFromState(TosuState state)
    {
        lock (_difficultyLock)
        {
            var bm = state?.Beatmap;
            if (bm == null) return;

        BeatmapID = bm.Id;
        BeatmapSetID = bm.Set;
        BeatmapTitle = bm.Title ?? "";
        BeatmapArtist = bm.Artist ?? "";
        BeatmapVersion = bm.Version ?? "";
        BeatmapHp = bm.Stats?.Hp?.Converted ?? bm.Stats?.Hp?.Original ?? 0;
        BeatmapString = $"{BeatmapArtist} - {BeatmapTitle} [{BeatmapVersion}]";
        BeatmapBpm = (int)Math.Round((double)(bm.Stats?.Bpm?.Common ?? 0));

        FirstHitObjectTime = bm.Time?.FirstObject ?? 0;

        decimal totalStars = bm.Stats?.Stars?.Total ?? 0;
        decimal liveStars = bm.Stats?.Stars?.Live ?? 0;
        BeatmapStars = totalStars > 0 ? totalStars : liveStars;
        BeatmapAim = bm.Stats?.Stars?.Aim ?? 0;
        BeatmapSpeed = bm.Stats?.Stars?.Speed ?? 0;

        BeatmapCs = bm.Stats?.Cs?.Converted ?? bm.Stats?.Cs?.Original ?? 0;
        BeatmapAr = bm.Stats?.Ar?.Converted ?? bm.Stats?.Ar?.Original ?? 0;
        BeatmapOd = bm.Stats?.Od?.Converted ?? bm.Stats?.Od?.Original ?? 0;

        if (!string.IsNullOrEmpty(bm.Checksum))
        {
            CurrentBeatmapChecksum = bm.Checksum;
        }
        }
    }

    public void FireUpdateDifficultyFromPpApi(int modNumber)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _ppApiCts, cts);
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        previous?.Dispose();
        _ = UpdateDifficultyFromPpApi(modNumber, cts.Token);
    }

    public async Task UpdateDifficultyFromPpApi(int modNumber, CancellationToken ct = default)
    {
        if (_tosuClient == null)
            return;

        string checksumAtCall;
        lock (_difficultyLock)
        {
            checksumAtCall = CurrentBeatmapChecksum;
        }

        try
        {
            var ppResult = await _tosuClient.CalculatePpAsync(modNumber, ct);
            ct.ThrowIfCancellationRequested();
            lock (_difficultyLock)
            {
                if (!string.IsNullOrEmpty(checksumAtCall) && CurrentBeatmapChecksum != checksumAtCall)
                    return;
                ApplyDifficultyResult(ppResult);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to update difficulty from PP API");
        }
    }

    private void ApplyDifficultyResult(PpCalcResult? ppResult)
    {
        var diff = ppResult?.Difficulty ?? ppResult?.Performance?.Difficulty;
        if (diff != null)
        {
            if (BeatmapAim == 0 && diff.Aim > 0) BeatmapAim = diff.Aim;
            if (BeatmapSpeed == 0 && diff.Speed > 0) BeatmapSpeed = diff.Speed;
            if (BeatmapStars == 0 && diff.Stars > 0) BeatmapStars = diff.Stars;
            if (BeatmapAr == 0 && diff.Ar > 0) BeatmapAr = diff.Ar;
            if (BeatmapOd == 0 && diff.Od > 0) BeatmapOd = diff.Od;
            if (BeatmapCs == 0 && diff.Cs > 0) BeatmapCs = diff.Cs;
            if (BeatmapHp == 0 && diff.Hp > 0) BeatmapHp = diff.Hp;
            if (BeatmapBpm == 0 && diff.Bpm > 0) BeatmapBpm = (int)Math.Round((double)diff.Bpm);
            if (diff.ClockRate > 0)
                LastClockRate = (float)diff.ClockRate;
        }
        if (ppResult?.Attributes != null)
        {
            var attr = ppResult.Attributes;
            if (BeatmapAr == 0 && attr.Ar > 0) BeatmapAr = attr.Ar;
            if (BeatmapOd == 0 && attr.Od > 0) BeatmapOd = attr.Od;
            if (BeatmapCs == 0 && attr.Cs > 0) BeatmapCs = attr.Cs;
            if (BeatmapHp == 0 && attr.Hp > 0) BeatmapHp = attr.Hp;
            if (BeatmapBpm == 0 && attr.Bpm > 0) BeatmapBpm = (int)Math.Round((double)attr.Bpm);
            if (attr.ClockRate > 0)
                LastClockRate = (float)attr.ClockRate;
        }
    }
}
