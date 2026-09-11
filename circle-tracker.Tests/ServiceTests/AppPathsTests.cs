using Circle_Tracker.Services;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class AppPathsTests
{
    public static IEnumerable<object[]> HostSanitizationCases => new List<object[]>
    {
        new object[] { default(string)!, "127.0.0.1" },
        new object[] { "", "127.0.0.1" },
        new object[] { "   ", "127.0.0.1" },
        new object[] { "  192.168.1.50  ", "192.168.1.50" },
        new object[] { "host with space", "127.0.0.1" },
        new object[] { "127.0.0.1", "127.0.0.1" },
        new object[] { "localhost", "localhost" }
    };

    public static IEnumerable<object[]> PortSanitizationCases => new List<object[]>
    {
        new object[] { 0, 24050 },
        new object[] { -1, 24050 },
        new object[] { 65536, 24050 },
        new object[] { 1, 1 },
        new object[] { 24050, 24050 },
        new object[] { 65535, 65535 },
        new object[] { 99999, 24050 }
    };
    [Fact]
    public void AppDataDirectory_ResolvesUnderLocalApplicationData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "circle-tracker");

        AppPaths.AppDataDirectory.Should().Be(expected);
    }

    [Fact]
    public void Migration_LegacyFileNextToBinary_MovesToAppData()
    {
        string legacyDir = Path.Combine(Path.GetTempPath(), $"ct_legacy_{Guid.NewGuid():N}");
        string targetDir = Path.Combine(Path.GetTempPath(), $"ct_target_{Guid.NewGuid():N}");
        Directory.CreateDirectory(legacyDir);
        try
        {
            File.WriteAllText(Path.Combine(legacyDir, "user_settings.json"), """{"username":"LegacyUser"}""");

            AppPaths.MigrateLegacyFiles(legacyDir, targetDir);

            File.ReadAllText(Path.Combine(targetDir, "user_settings.json")).Should().Be("""{"username":"LegacyUser"}""");
        }
        finally
        {
            try { Directory.Delete(legacyDir, true); } catch { }
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    [Fact]
    public void Migration_LegacyFileNextToBinary_RemovesOriginal()
    {
        string legacyDir = Path.Combine(Path.GetTempPath(), $"ct_legacy_{Guid.NewGuid():N}");
        string targetDir = Path.Combine(Path.GetTempPath(), $"ct_target_{Guid.NewGuid():N}");
        Directory.CreateDirectory(legacyDir);
        try
        {
            File.WriteAllText(Path.Combine(legacyDir, "credentials.json"), """{"installed":{}}""");

            AppPaths.MigrateLegacyFiles(legacyDir, targetDir);

            File.Exists(Path.Combine(legacyDir, "credentials.json")).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(legacyDir, true); } catch { }
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    [Fact]
    public void Migration_ExistingNewerFileInAppData_DoesNotOverwrite()
    {
        string legacyDir = Path.Combine(Path.GetTempPath(), $"ct_legacy_{Guid.NewGuid():N}");
        string targetDir = Path.Combine(Path.GetTempPath(), $"ct_target_{Guid.NewGuid():N}");
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(targetDir);
        try
        {
            File.WriteAllText(Path.Combine(legacyDir, "user_settings.json"), """{"username":"LegacyUser"}""");
            File.WriteAllText(Path.Combine(targetDir, "user_settings.json"), """{"username":"CurrentUser"}""");

            AppPaths.MigrateLegacyFiles(legacyDir, targetDir);

            File.ReadAllText(Path.Combine(targetDir, "user_settings.json")).Should().Be("""{"username":"CurrentUser"}""");
        }
        finally
        {
            try { Directory.Delete(legacyDir, true); } catch { }
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    [Fact]
    public void Migration_ExistingNewerFileInAppData_LeavesLegacySourceInPlace()
    {
        string legacyDir = Path.Combine(Path.GetTempPath(), $"ct_legacy_{Guid.NewGuid():N}");
        string targetDir = Path.Combine(Path.GetTempPath(), $"ct_target_{Guid.NewGuid():N}");
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(targetDir);
        try
        {
            File.WriteAllText(Path.Combine(legacyDir, "circle_tracker.db"), "legacy-bytes");
            File.WriteAllText(Path.Combine(targetDir, "circle_tracker.db"), "current-bytes");

            AppPaths.MigrateLegacyFiles(legacyDir, targetDir);

            File.Exists(Path.Combine(legacyDir, "circle_tracker.db")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(legacyDir, true); } catch { }
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    [Fact]
    public void LegacyTxt_MigratesToJson()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"ct_txtjson_{Guid.NewGuid():N}.json");
        string tempTxt = Path.Combine(Path.GetTempPath(), $"ct_txtjson_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllLines(tempTxt, new[] { "sheet-123", "Raw Data", "1", "0", "0", "TxtUser", "10.0.0.5", "25050" });

            var service = new SettingsService(tempJson, tempTxt);

            File.Exists(tempJson).Should().BeTrue();
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
            try { if (File.Exists(tempTxt)) File.Delete(tempTxt); } catch { }
        }
    }

    [Fact]
    public void LegacyTxt_MigratesToJson_LoadsMigratedUsername()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"ct_txtuser_{Guid.NewGuid():N}.json");
        string tempTxt = Path.Combine(Path.GetTempPath(), $"ct_txtuser_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllLines(tempTxt, new[] { "sheet-123", "Raw Data", "1", "0", "0", "TxtUser", "10.0.0.5", "25050" });

            var service = new SettingsService(tempJson, tempTxt);

            service.Username.Should().Be("TxtUser");
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
            try { if (File.Exists(tempTxt)) File.Delete(tempTxt); } catch { }
        }
    }

    [Fact]
    public void BadHostPort_SanitizesToDefaults()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"ct_badhost_{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(tempJson, """{"tosuHost":"   ","tosuPort":-5}""");

            var service = new SettingsService(tempJson, tempJson + ".old");

            service.TosuHost.Should().Be("127.0.0.1");
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
        }
    }

    [Fact]
    public void BadHostPort_SanitizesToDefaults_PortFallsBack()
    {
        string tempJson = Path.Combine(Path.GetTempPath(), $"ct_badport_{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(tempJson, """{"tosuHost":"   ","tosuPort":-5}""");

            var service = new SettingsService(tempJson, tempJson + ".old");

            service.TosuPort.Should().Be(24050);
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { }
        }
    }

    [Fact]
    public void SanitizeTosuHost_ValidHost_PreservesTrimmedValue()
    {
        string result = SettingsService.SanitizeTosuHost("  192.168.1.50  ");

        result.Should().Be("192.168.1.50");
    }

    [Fact]
    public void SanitizeTosuPort_ValidPort_PreservesValue()
    {
        int result = SettingsService.SanitizeTosuPort(12345);

        result.Should().Be(12345);
    }

    [Theory]
    [MemberData(nameof(HostSanitizationCases))]
    public void SanitizeTosuHost_VariousInputs_ReturnsExpected(string? input, string expected)
    {
        string result = SettingsService.SanitizeTosuHost(input);

        result.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(PortSanitizationCases))]
    public void SanitizeTosuPort_VariousInputs_ReturnsExpected(int input, int expected)
    {
        int result = SettingsService.SanitizeTosuPort(input);

        result.Should().Be(expected);
    }
}
