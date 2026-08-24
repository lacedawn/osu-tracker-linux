using Newtonsoft.Json;
using System.Collections.Generic;

namespace Circle_Tracker
{
    public class TosuState
    {
        [JsonProperty("state")] public TosuGameState? State { get; set; }
        [JsonProperty("session")] public TosuSession? Session { get; set; }
        [JsonProperty("settings")] public TosuSettings? Settings { get; set; }
        [JsonProperty("profile")] public TosuProfile? Profile { get; set; }
        [JsonProperty("beatmap")] public TosuBeatmap? Beatmap { get; set; }
        [JsonProperty("play")] public TosuPlay? Play { get; set; }
        [JsonProperty("files")] public TosuFiles? Files { get; set; }
    }

    public class TosuGameState
    {
        [JsonProperty("number")] public int Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = "";
    }

    public class TosuSession
    {
        [JsonProperty("playTime")] public int PlayTime { get; set; }
        [JsonProperty("playCount")] public int PlayCount { get; set; }
    }

    public class TosuNumberName
    {
        [JsonProperty("number")] public int Number { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = "";
    }

    public class TosuClientInfo
    {
        [JsonProperty("version")] public string? Version { get; set; }
    }

    public class TosuSettings
    {
        [JsonProperty("replayUIVisible")] public bool ReplayUIVisible { get; set; }
        [JsonProperty("mode")] public TosuNumberName? Mode { get; set; }
        [JsonProperty("client")] public TosuClientInfo? Client { get; set; }
    }

    public class TosuProfile
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string? Name { get; set; }
    }

    public class TosuBeatmapTime
    {
        [JsonProperty("live")] public int Live { get; set; }
        [JsonProperty("firstObject")] public int FirstObject { get; set; }
        [JsonProperty("lastObject")] public int LastObject { get; set; }
    }

    public class TosuStars
    {
        [JsonProperty("live")] public decimal Live { get; set; }
        [JsonProperty("aim")] public decimal Aim { get; set; }
        [JsonProperty("speed")] public decimal Speed { get; set; }
        [JsonProperty("total")] public decimal Total { get; set; }
    }

    public class TosuStatValue
    {
        [JsonProperty("original")] public decimal Original { get; set; }
        [JsonProperty("converted")] public decimal Converted { get; set; }
    }

    public class TosuBpm
    {
        [JsonProperty("common")] public decimal Common { get; set; }
        [JsonProperty("min")] public decimal Min { get; set; }
        [JsonProperty("max")] public decimal Max { get; set; }
    }

    public class TosuBeatmapStats
    {
        [JsonProperty("ar")] public TosuStatValue? Ar { get; set; }
        [JsonProperty("cs")] public TosuStatValue? Cs { get; set; }
        [JsonProperty("od")] public TosuStatValue? Od { get; set; }
        [JsonProperty("hp")] public TosuStatValue? Hp { get; set; }
        [JsonProperty("bpm")] public TosuBpm? Bpm { get; set; }
        [JsonProperty("stars")] public TosuStars? Stars { get; set; }
    }

    public class TosuBeatmap
    {
        [JsonProperty("time")] public TosuBeatmapTime? Time { get; set; }
        [JsonProperty("checksum")] public string? Checksum { get; set; }
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("set")] public int Set { get; set; }
        [JsonProperty("artist")] public string? Artist { get; set; }
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("version")] public string? Version { get; set; }
        [JsonProperty("mapper")] public string? Mapper { get; set; }
        [JsonProperty("stats")] public TosuBeatmapStats? Stats { get; set; }
    }

    public class TosuHits
    {
        [JsonProperty("300")] public int H300 { get; set; }
        [JsonProperty("100")] public int H100 { get; set; }
        [JsonProperty("50")] public int H50 { get; set; }
        [JsonProperty("0")] public int Misses { get; set; }
    }

    public class TosuPlayMods
    {
        [JsonProperty("number")] public int Number { get; set; }
        [JsonProperty("name")] public string? Name { get; set; }
    }

    public class TosuPlay
    {
        [JsonProperty("playerName")] public string? PlayerName { get; set; }
        [JsonProperty("mode")] public TosuNumberName? Mode { get; set; }
        [JsonProperty("score")] public long Score { get; set; }
        [JsonProperty("accuracy")] public decimal Accuracy { get; set; }
        [JsonProperty("mods")] public TosuPlayMods? Mods { get; set; }
        [JsonProperty("hits")] public TosuHits? Hits { get; set; }
    }

    public class TosuFiles
    {
        [JsonProperty("beatmap")] public string? Beatmap { get; set; }
    }

    public class PpAttributes
    {
        [JsonProperty("mode")] public int Mode { get; set; }
        [JsonProperty("ar")] public decimal Ar { get; set; }
        [JsonProperty("cs")] public decimal Cs { get; set; }
        [JsonProperty("hp")] public decimal Hp { get; set; }
        [JsonProperty("od")] public decimal Od { get; set; }
        [JsonProperty("clockRate")] public double ClockRate { get; set; }
        [JsonProperty("bpm")] public decimal Bpm { get; set; }
    }

    public class PpDifficulty
    {
        [JsonProperty("mode")] public int Mode { get; set; }
        [JsonProperty("aim")] public decimal Aim { get; set; }
        [JsonProperty("speed")] public decimal Speed { get; set; }
        [JsonProperty("flashlight")] public decimal Flashlight { get; set; }
        [JsonProperty("sliderFactor")] public decimal SliderFactor { get; set; }
        [JsonProperty("ar")] public decimal Ar { get; set; }
        [JsonProperty("od")] public decimal Od { get; set; }
        [JsonProperty("hp")] public decimal Hp { get; set; }
        [JsonProperty("cs")] public decimal Cs { get; set; }
        [JsonProperty("bpm")] public decimal Bpm { get; set; }
        [JsonProperty("clockRate")] public double ClockRate { get; set; }
        [JsonProperty("stars")] public decimal Stars { get; set; }
    }

    public class PpPerformance
    {
        [JsonProperty("mode")] public int Mode { get; set; }
        [JsonProperty("pp")] public double Pp { get; set; }
        [JsonProperty("ppAcc")] public double PpAcc { get; set; }
        [JsonProperty("ppAim")] public double PpAim { get; set; }
        [JsonProperty("ppSpeed")] public double PpSpeed { get; set; }
        [JsonProperty("difficulty")] public PpDifficulty? Difficulty { get; set; }
    }

    public class PpCalcResult
    {
        [JsonProperty("attributes")] public PpAttributes? Attributes { get; set; }
        [JsonProperty("performance")] public PpPerformance? Performance { get; set; }
        [JsonProperty("difficulty")] public PpDifficulty? Difficulty { get; set; }
        [JsonProperty("pp")] public double? Pp { get; set; }
    }
}
