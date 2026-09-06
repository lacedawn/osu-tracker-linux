using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Circle_Tracker
{
    public class TosuState
    {
        [JsonPropertyName("state")]
        public TosuGameState? State { get; set; }

        [JsonPropertyName("session")]
        public TosuSession? Session { get; set; }

        [JsonPropertyName("settings")]
        public TosuSettings? Settings { get; set; }

        [JsonPropertyName("profile")]
        public TosuProfile? Profile { get; set; }

        [JsonPropertyName("beatmap")]
        public TosuBeatmap? Beatmap { get; set; }

        [JsonPropertyName("play")]
        public TosuPlay? Play { get; set; }

        [JsonPropertyName("files")]
        public TosuFiles? Files { get; set; }
    }

    public class TosuGameState
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class TosuSession
    {
        [JsonPropertyName("playTime")]
        public int PlayTime { get; set; }

        [JsonPropertyName("playCount")]
        public int PlayCount { get; set; }
    }

    public class TosuNumberName
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class TosuClientInfo
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    public class TosuSettings
    {
        [JsonPropertyName("replayUIVisible")]
        public bool ReplayUIVisible { get; set; }

        [JsonPropertyName("mode")]
        public TosuNumberName? Mode { get; set; }

        [JsonPropertyName("client")]
        public TosuClientInfo? Client { get; set; }
    }

    public class TosuProfile
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TosuBeatmapTime
    {
        [JsonPropertyName("live")]
        public int Live { get; set; }

        [JsonPropertyName("firstObject")]
        public int FirstObject { get; set; }

        [JsonPropertyName("lastObject")]
        public int LastObject { get; set; }
    }

    public class TosuStars
    {
        [JsonPropertyName("live")]
        public decimal Live { get; set; }

        [JsonPropertyName("aim")]
        public decimal Aim { get; set; }

        [JsonPropertyName("speed")]
        public decimal Speed { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }
    }

    public class TosuStatValue
    {
        [JsonPropertyName("original")]
        public decimal Original { get; set; }

        [JsonPropertyName("converted")]
        public decimal Converted { get; set; }
    }

    public class TosuBpm
    {
        [JsonPropertyName("common")]
        public decimal Common { get; set; }

        [JsonPropertyName("min")]
        public decimal Min { get; set; }

        [JsonPropertyName("max")]
        public decimal Max { get; set; }
    }

    public class TosuBeatmapStats
    {
        [JsonPropertyName("ar")]
        public TosuStatValue? Ar { get; set; }

        [JsonPropertyName("cs")]
        public TosuStatValue? Cs { get; set; }

        [JsonPropertyName("od")]
        public TosuStatValue? Od { get; set; }

        [JsonPropertyName("hp")]
        public TosuStatValue? Hp { get; set; }

        [JsonPropertyName("bpm")]
        public TosuBpm? Bpm { get; set; }

        [JsonPropertyName("stars")]
        public TosuStars? Stars { get; set; }
    }

    public class TosuBeatmap
    {
        [JsonPropertyName("time")]
        public TosuBeatmapTime? Time { get; set; }

        [JsonPropertyName("checksum")]
        public string? Checksum { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("set")]
        public int Set { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("mapper")]
        public string? Mapper { get; set; }

        [JsonPropertyName("stats")]
        public TosuBeatmapStats? Stats { get; set; }
    }

    public class TosuHits
    {
        [JsonPropertyName("300")]
        public int H300 { get; set; }

        [JsonPropertyName("100")]
        public int H100 { get; set; }

        [JsonPropertyName("50")]
        public int H50 { get; set; }

        [JsonPropertyName("0")]
        public int Misses { get; set; }
    }

    public class TosuPlayMods
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TosuPlay
    {
        [JsonPropertyName("playerName")]
        public string? PlayerName { get; set; }

        [JsonPropertyName("mode")]
        public TosuNumberName? Mode { get; set; }

        [JsonPropertyName("score")]
        public long Score { get; set; }

        [JsonPropertyName("accuracy")]
        public decimal Accuracy { get; set; }

        [JsonPropertyName("mods")]
        public TosuPlayMods? Mods { get; set; }

        [JsonPropertyName("hits")]
        public TosuHits? Hits { get; set; }
    }

    public class TosuFiles
    {
        [JsonPropertyName("beatmap")]
        public string? Beatmap { get; set; }
    }

    public class PpAttributes
    {
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonPropertyName("ar")]
        public decimal Ar { get; set; }

        [JsonPropertyName("cs")]
        public decimal Cs { get; set; }

        [JsonPropertyName("hp")]
        public decimal Hp { get; set; }

        [JsonPropertyName("od")]
        public decimal Od { get; set; }

        [JsonPropertyName("clockRate")]
        public double ClockRate { get; set; }

        [JsonPropertyName("bpm")]
        public decimal Bpm { get; set; }
    }

    public class PpDifficulty
    {
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonPropertyName("aim")]
        public decimal Aim { get; set; }

        [JsonPropertyName("speed")]
        public decimal Speed { get; set; }

        [JsonPropertyName("flashlight")]
        public decimal Flashlight { get; set; }

        [JsonPropertyName("sliderFactor")]
        public decimal SliderFactor { get; set; }

        [JsonPropertyName("ar")]
        public decimal Ar { get; set; }

        [JsonPropertyName("od")]
        public decimal Od { get; set; }

        [JsonPropertyName("hp")]
        public decimal Hp { get; set; }

        [JsonPropertyName("cs")]
        public decimal Cs { get; set; }

        [JsonPropertyName("bpm")]
        public decimal Bpm { get; set; }

        [JsonPropertyName("clockRate")]
        public double ClockRate { get; set; }

        [JsonPropertyName("stars")]
        public decimal Stars { get; set; }
    }

    public class PpPerformance
    {
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonPropertyName("pp")]
        public double Pp { get; set; }

        [JsonPropertyName("ppAcc")]
        public double PpAcc { get; set; }

        [JsonPropertyName("ppAim")]
        public double PpAim { get; set; }

        [JsonPropertyName("ppSpeed")]
        public double PpSpeed { get; set; }

        [JsonPropertyName("difficulty")]
        public PpDifficulty? Difficulty { get; set; }
    }

    public class PpCalcResult
    {
        [JsonPropertyName("attributes")]
        public PpAttributes? Attributes { get; set; }

        [JsonPropertyName("performance")]
        public PpPerformance? Performance { get; set; }

        [JsonPropertyName("difficulty")]
        public PpDifficulty? Difficulty { get; set; }

        [JsonPropertyName("pp")]
        public double? Pp { get; set; }
    }
}
