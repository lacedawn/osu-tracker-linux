using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Circle_Tracker
{
    public class TosuState
    {
        [JsonProperty("state")]
        [JsonPropertyName("state")]
        public TosuGameState? State { get; set; }

        [JsonProperty("session")]
        [JsonPropertyName("session")]
        public TosuSession? Session { get; set; }

        [JsonProperty("settings")]
        [JsonPropertyName("settings")]
        public TosuSettings? Settings { get; set; }

        [JsonProperty("profile")]
        [JsonPropertyName("profile")]
        public TosuProfile? Profile { get; set; }

        [JsonProperty("beatmap")]
        [JsonPropertyName("beatmap")]
        public TosuBeatmap? Beatmap { get; set; }

        [JsonProperty("play")]
        [JsonPropertyName("play")]
        public TosuPlay? Play { get; set; }

        [JsonProperty("files")]
        [JsonPropertyName("files")]
        public TosuFiles? Files { get; set; }
    }

    public class TosuGameState
    {
        [JsonProperty("number")]
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class TosuSession
    {
        [JsonProperty("playTime")]
        [JsonPropertyName("playTime")]
        public int PlayTime { get; set; }

        [JsonProperty("playCount")]
        [JsonPropertyName("playCount")]
        public int PlayCount { get; set; }
    }

    public class TosuNumberName
    {
        [JsonProperty("number")]
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class TosuClientInfo
    {
        [JsonProperty("version")]
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    public class TosuSettings
    {
        [JsonProperty("replayUIVisible")]
        [JsonPropertyName("replayUIVisible")]
        public bool ReplayUIVisible { get; set; }

        [JsonProperty("mode")]
        [JsonPropertyName("mode")]
        public TosuNumberName? Mode { get; set; }

        [JsonProperty("client")]
        [JsonPropertyName("client")]
        public TosuClientInfo? Client { get; set; }
    }

    public class TosuProfile
    {
        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TosuBeatmapTime
    {
        [JsonProperty("live")]
        [JsonPropertyName("live")]
        public int Live { get; set; }

        [JsonProperty("firstObject")]
        [JsonPropertyName("firstObject")]
        public int FirstObject { get; set; }

        [JsonProperty("lastObject")]
        [JsonPropertyName("lastObject")]
        public int LastObject { get; set; }
    }

    public class TosuStars
    {
        [JsonProperty("live")]
        [JsonPropertyName("live")]
        public decimal Live { get; set; }

        [JsonProperty("aim")]
        [JsonPropertyName("aim")]
        public decimal Aim { get; set; }

        [JsonProperty("speed")]
        [JsonPropertyName("speed")]
        public decimal Speed { get; set; }

        [JsonProperty("total")]
        [JsonPropertyName("total")]
        public decimal Total { get; set; }
    }

    public class TosuStatValue
    {
        [JsonProperty("original")]
        [JsonPropertyName("original")]
        public decimal Original { get; set; }

        [JsonProperty("converted")]
        [JsonPropertyName("converted")]
        public decimal Converted { get; set; }
    }

    public class TosuBpm
    {
        [JsonProperty("common")]
        [JsonPropertyName("common")]
        public decimal Common { get; set; }

        [JsonProperty("min")]
        [JsonPropertyName("min")]
        public decimal Min { get; set; }

        [JsonProperty("max")]
        [JsonPropertyName("max")]
        public decimal Max { get; set; }
    }

    public class TosuBeatmapStats
    {
        [JsonProperty("ar")]
        [JsonPropertyName("ar")]
        public TosuStatValue? Ar { get; set; }

        [JsonProperty("cs")]
        [JsonPropertyName("cs")]
        public TosuStatValue? Cs { get; set; }

        [JsonProperty("od")]
        [JsonPropertyName("od")]
        public TosuStatValue? Od { get; set; }

        [JsonProperty("hp")]
        [JsonPropertyName("hp")]
        public TosuStatValue? Hp { get; set; }

        [JsonProperty("bpm")]
        [JsonPropertyName("bpm")]
        public TosuBpm? Bpm { get; set; }

        [JsonProperty("stars")]
        [JsonPropertyName("stars")]
        public TosuStars? Stars { get; set; }
    }

    public class TosuBeatmap
    {
        [JsonProperty("time")]
        [JsonPropertyName("time")]
        public TosuBeatmapTime? Time { get; set; }

        [JsonProperty("checksum")]
        [JsonPropertyName("checksum")]
        public string? Checksum { get; set; }

        [JsonProperty("id")]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonProperty("set")]
        [JsonPropertyName("set")]
        public int Set { get; set; }

        [JsonProperty("artist")]
        [JsonPropertyName("artist")]
        public string? Artist { get; set; }

        [JsonProperty("title")]
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonProperty("version")]
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonProperty("mapper")]
        [JsonPropertyName("mapper")]
        public string? Mapper { get; set; }

        [JsonProperty("stats")]
        [JsonPropertyName("stats")]
        public TosuBeatmapStats? Stats { get; set; }
    }

    public class TosuHits
    {
        [JsonProperty("300")]
        [JsonPropertyName("300")]
        public int H300 { get; set; }

        [JsonProperty("100")]
        [JsonPropertyName("100")]
        public int H100 { get; set; }

        [JsonProperty("50")]
        [JsonPropertyName("50")]
        public int H50 { get; set; }

        [JsonProperty("0")]
        [JsonPropertyName("0")]
        public int Misses { get; set; }
    }

    public class TosuPlayMods
    {
        [JsonProperty("number")]
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonProperty("name")]
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class TosuPlay
    {
        [JsonProperty("playerName")]
        [JsonPropertyName("playerName")]
        public string? PlayerName { get; set; }

        [JsonProperty("mode")]
        [JsonPropertyName("mode")]
        public TosuNumberName? Mode { get; set; }

        [JsonProperty("score")]
        [JsonPropertyName("score")]
        public long Score { get; set; }

        [JsonProperty("accuracy")]
        [JsonPropertyName("accuracy")]
        public decimal Accuracy { get; set; }

        [JsonProperty("mods")]
        [JsonPropertyName("mods")]
        public TosuPlayMods? Mods { get; set; }

        [JsonProperty("hits")]
        [JsonPropertyName("hits")]
        public TosuHits? Hits { get; set; }
    }

    public class TosuFiles
    {
        [JsonProperty("beatmap")]
        [JsonPropertyName("beatmap")]
        public string? Beatmap { get; set; }
    }

    public class PpAttributes
    {
        [JsonProperty("mode")]
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonProperty("ar")]
        [JsonPropertyName("ar")]
        public decimal Ar { get; set; }

        [JsonProperty("cs")]
        [JsonPropertyName("cs")]
        public decimal Cs { get; set; }

        [JsonProperty("hp")]
        [JsonPropertyName("hp")]
        public decimal Hp { get; set; }

        [JsonProperty("od")]
        [JsonPropertyName("od")]
        public decimal Od { get; set; }

        [JsonProperty("clockRate")]
        [JsonPropertyName("clockRate")]
        public double ClockRate { get; set; }

        [JsonProperty("bpm")]
        [JsonPropertyName("bpm")]
        public decimal Bpm { get; set; }
    }

    public class PpDifficulty
    {
        [JsonProperty("mode")]
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonProperty("aim")]
        [JsonPropertyName("aim")]
        public decimal Aim { get; set; }

        [JsonProperty("speed")]
        [JsonPropertyName("speed")]
        public decimal Speed { get; set; }

        [JsonProperty("flashlight")]
        [JsonPropertyName("flashlight")]
        public decimal Flashlight { get; set; }

        [JsonProperty("sliderFactor")]
        [JsonPropertyName("sliderFactor")]
        public decimal SliderFactor { get; set; }

        [JsonProperty("ar")]
        [JsonPropertyName("ar")]
        public decimal Ar { get; set; }

        [JsonProperty("od")]
        [JsonPropertyName("od")]
        public decimal Od { get; set; }

        [JsonProperty("hp")]
        [JsonPropertyName("hp")]
        public decimal Hp { get; set; }

        [JsonProperty("cs")]
        [JsonPropertyName("cs")]
        public decimal Cs { get; set; }

        [JsonProperty("bpm")]
        [JsonPropertyName("bpm")]
        public decimal Bpm { get; set; }

        [JsonProperty("clockRate")]
        [JsonPropertyName("clockRate")]
        public double ClockRate { get; set; }

        [JsonProperty("stars")]
        [JsonPropertyName("stars")]
        public decimal Stars { get; set; }
    }

    public class PpPerformance
    {
        [JsonProperty("mode")]
        [JsonPropertyName("mode")]
        public int Mode { get; set; }

        [JsonProperty("pp")]
        [JsonPropertyName("pp")]
        public double Pp { get; set; }

        [JsonProperty("ppAcc")]
        [JsonPropertyName("ppAcc")]
        public double PpAcc { get; set; }

        [JsonProperty("ppAim")]
        [JsonPropertyName("ppAim")]
        public double PpAim { get; set; }

        [JsonProperty("ppSpeed")]
        [JsonPropertyName("ppSpeed")]
        public double PpSpeed { get; set; }

        [JsonProperty("difficulty")]
        [JsonPropertyName("difficulty")]
        public PpDifficulty? Difficulty { get; set; }
    }

    public class PpCalcResult
    {
        [JsonProperty("attributes")]
        [JsonPropertyName("attributes")]
        public PpAttributes? Attributes { get; set; }

        [JsonProperty("performance")]
        [JsonPropertyName("performance")]
        public PpPerformance? Performance { get; set; }

        [JsonProperty("difficulty")]
        [JsonPropertyName("difficulty")]
        public PpDifficulty? Difficulty { get; set; }

        [JsonProperty("pp")]
        [JsonPropertyName("pp")]
        public double? Pp { get; set; }
    }
}
