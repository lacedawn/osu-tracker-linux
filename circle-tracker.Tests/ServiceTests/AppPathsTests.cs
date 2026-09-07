using Circle_Tracker.Services;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace CircleTracker.Tests.ServiceTests;

public class AppPathsTests
{
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
}
