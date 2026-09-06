using Circle_Tracker;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace CircleTracker.Tests.SerializationTests;

public class JsonSerializationTests
{
    [Fact]
    public void TosuState_RoundTrip_PreservesAllData()
    {
        var original = new TosuState
        {
            State = new TosuGameState { Number = 2, Name = "play" },
            Session = new TosuSession { PlayTime = 1200, PlayCount = 15 },
            Settings = new TosuSettings
            {
                ReplayUIVisible = false,
                Mode = new TosuNumberName { Number = 0, Name = "osu" },
                Client = new TosuClientInfo { Version = "20240101" }
            },
            Profile = new TosuProfile { Id = 42, Name = "TestUser" },
            Beatmap = new TosuBeatmap
            {
                Id = 12345,
                Set = 6789,
                Artist = "ArtistName",
                Title = "SongTitle",
                Version = "Extra",
                Mapper = "MapperName",
                Checksum = "abcdef123456",
                Time = new TosuBeatmapTime { Live = 100, FirstObject = 10, LastObject = 180 },
                Stats = new TosuBeatmapStats
                {
                    Ar = new TosuStatValue { Original = 9.0m, Converted = 9.0m },
                    Cs = new TosuStatValue { Original = 4.0m, Converted = 4.0m },
                    Od = new TosuStatValue { Original = 8.0m, Converted = 8.0m },
                    Hp = new TosuStatValue { Original = 6.0m, Converted = 6.0m },
                    Bpm = new TosuBpm { Common = 180m, Min = 90m, Max = 180m },
                    Stars = new TosuStars { Live = 5.5m, Aim = 2.8m, Speed = 2.7m, Total = 5.5m }
                }
            },
            Play = new TosuPlay
            {
                PlayerName = "TestUser",
                Score = 1500000,
                Accuracy = 99.25m,
                Mode = new TosuNumberName { Number = 0, Name = "osu" },
                Mods = new TosuPlayMods { Number = 8, Name = "HD" },
                Hits = new TosuHits { H300 = 500, H100 = 10, H50 = 1, Misses = 0 }
            },
            Files = new TosuFiles { Beatmap = "song.osu" }
        };

        string json = JsonSerializer.Serialize(original, TosuClient.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<TosuState>(json, TosuClient.SerializerOptions);

        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void PpCalcResult_DeserializeSampleJson_PopulatesAllPropertiesCorrectly()
    {
        string sampleJson = """
        {
          "attributes": {
            "mode": 0,
            "ar": 9.5,
            "cs": 4.0,
            "hp": 6.0,
            "od": 8.0,
            "clockRate": 1.0,
            "bpm": 180.0
          },
          "performance": {
            "mode": 0,
            "pp": 321.45,
            "ppAcc": 100.1,
            "ppAim": 150.2,
            "ppSpeed": 71.15,
            "difficulty": {
              "mode": 0,
              "aim": 2.5,
              "speed": 2.3,
              "flashlight": 0.0,
              "sliderFactor": 1.0,
              "ar": 9.5,
              "od": 8.0,
              "hp": 6.0,
              "cs": 4.0,
              "bpm": 180.0,
              "clockRate": 1.0,
              "stars": 5.8
            }
          },
          "difficulty": {
            "mode": 0,
            "aim": 2.5,
            "speed": 2.3,
            "flashlight": 0.0,
            "sliderFactor": 1.0,
            "ar": 9.5,
            "od": 8.0,
            "hp": 6.0,
            "cs": 4.0,
            "bpm": 180.0,
            "clockRate": 1.0,
            "stars": 5.8
          },
          "pp": 321.45
        }
        """;

        var result = JsonSerializer.Deserialize<PpCalcResult>(sampleJson, TosuClient.SerializerOptions);

        result.Should().NotBeNull();
        result!.Pp.Should().Be(321.45);
        result.Attributes.Should().NotBeNull();
        result.Attributes!.Ar.Should().Be(9.5m);
        result.Attributes.Bpm.Should().Be(180.0m);
        result.Performance.Should().NotBeNull();
        result.Performance!.Pp.Should().Be(321.45);
        result.Performance.PpAim.Should().Be(150.2);
        result.Difficulty.Should().NotBeNull();
        result.Difficulty!.Stars.Should().Be(5.8m);
        result.Performance.Difficulty!.Stars.Should().Be(5.8m);
    }

    [Fact]
    public void UserSettings_RoundTrip_PreservesAllProperties()
    {
        var original = new UserSettings
        {
            EnableLocalLogging = false,
            EnableGoogleSheetsLogging = true,
            LocalDatabasePath = "/custom/path/db.sqlite",
            SpreadsheetId = "custom_spreadsheet_id_123",
            SheetName = "CustomPlays",
            SubmitSoundEnabled = false,
            SpreadsheetTimezoneVerified = true,
            UseAltFuncSeparator = true,
            Username = "TestPlayer",
            TosuHost = "192.168.1.50",
            TosuPort = 12345,
            DisableBackgroundAnimationsWhenUnfocused = true,
            UpdateRepository = "custom/custom-repo"
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<UserSettings>(json);

        deserialized.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Release_DeserializeGitHubApiResponse_PopulatesProperties()
    {
        string gitHubJson = """
        {
          "tag_name": "v2.5.0",
          "html_url": "https://github.com/lacedawn/osu-tracker-linux/releases/tag/v2.5.0",
          "body": "Detailed release notes here"
        }
        """;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var release = JsonSerializer.Deserialize<Release>(gitHubJson, options);

        release.Should().NotBeNull();
        release!.TagName.Should().Be("v2.5.0");
        release.HtmlUrl.Should().Be("https://github.com/lacedawn/osu-tracker-linux/releases/tag/v2.5.0");
        release.Body.Should().Be("Detailed release notes here");
    }

    [Fact]
    public void UserSettings_Serialize_ProducesCamelCasePropertyNamesMatchingAttributes()
    {
        var settings = new UserSettings();

        string json = JsonSerializer.Serialize(settings);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("enableLocalLogging", out _).Should().BeTrue();
        root.TryGetProperty("enableGoogleSheetsLogging", out _).Should().BeTrue();
        root.TryGetProperty("localDatabasePath", out _).Should().BeTrue();
        root.TryGetProperty("spreadsheetId", out _).Should().BeTrue();
        root.TryGetProperty("sheetName", out _).Should().BeTrue();
        root.TryGetProperty("submitSoundEnabled", out _).Should().BeTrue();
        root.TryGetProperty("spreadsheetTimezoneVerified", out _).Should().BeTrue();
        root.TryGetProperty("useAltFuncSeparator", out _).Should().BeTrue();
        root.TryGetProperty("username", out _).Should().BeTrue();
        root.TryGetProperty("tosuHost", out _).Should().BeTrue();
        root.TryGetProperty("tosuPort", out _).Should().BeTrue();
        root.TryGetProperty("disableBackgroundAnimationsWhenUnfocused", out _).Should().BeTrue();
        root.TryGetProperty("updateRepository", out _).Should().BeTrue();
        root.TryGetProperty("EnableLocalLogging", out _).Should().BeFalse();
    }
}
