using Circle_Tracker;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.PlatformTests;

public class SoundHelperTests
{
    private static string GetTestSoundPath()
    {
        string directPath = Path.Combine(AppContext.BaseDirectory, "assets", "sectionpass.wav");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        string fallbackPath = Path.Combine("assets", "sectionpass.wav");
        if (File.Exists(fallbackPath))
        {
            return Path.GetFullPath(fallbackPath);
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"test_sound_{Guid.NewGuid():N}.wav");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(0);
        File.WriteAllBytes(tempPath, stream.ToArray());
        return tempPath;
    }

    [Fact]
    public async Task PlaySoundAsync_WhenFileDoesNotExist_LogsWarningAndDoesNotThrow()
    {
        Func<Task> act = async () => await SoundHelper.PlaySoundAsync("/path/to/nonexistent/file.wav");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PlaySoundAsync_ConcurrentCalls_QueuesOrPlaysWithoutDeviceCollision()
    {
        string soundPath = GetTestSoundPath();

        var tasks = Enumerable.Range(0, 10).Select(_ => SoundHelper.PlaySoundAsync(soundPath));
        Func<Task> act = async () => await Task.WhenAll(tasks);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PlaySoundAsync_ExecutesWithoutSpawningPowerShellOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        int runningBefore = Process.GetProcessesByName("powershell").Length;
        string soundPath = GetTestSoundPath();

        await SoundHelper.PlaySoundAsync(soundPath);

        int runningAfter = Process.GetProcessesByName("powershell").Length;
        runningAfter.Should().Be(runningBefore);
    }

    [Fact]
    public void PlaySound_WhenFileDoesNotExist_DoesNotThrow()
    {
        Action act = () => SoundHelper.PlaySound("/path/to/nonexistent/file.wav");

        act.Should().NotThrow();
    }

    [Fact]
    public void PreloadSound_WhenFileExists_DoesNotThrow()
    {
        string soundPath = GetTestSoundPath();

        Action act = () => SoundHelper.PreloadSound(soundPath);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task PlaySoundAsync_EmptyPath_LogsWarningAndReturns()
    {
        Func<Task> act = async () => await SoundHelper.PlaySoundAsync("");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PlaySoundAsync_NullPath_LogsWarningAndReturns()
    {
        Func<Task> act = async () => await SoundHelper.PlaySoundAsync(null!);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void PreloadSound_NonexistentFile_DoesNotThrow()
    {
        Action act = () => SoundHelper.PreloadSound("/path/to/nonexistent/preload.wav");

        act.Should().NotThrow();
    }

    [Fact]
    public void Shutdown_CalledMultipleTimes_DoesNotThrow()
    {
        Action act = () =>
        {
            SoundHelper.Shutdown();
            SoundHelper.Shutdown();
            SoundHelper.Shutdown();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void PlaySound_EmptyPath_DoesNotThrow()
    {
        Action act = () => SoundHelper.PlaySound("");

        act.Should().NotThrow();
    }

    [Fact]
    public void PlaySound_NullPath_DoesNotThrow()
    {
        Action act = () => SoundHelper.PlaySound(null!);

        act.Should().NotThrow();
    }

    [Fact]
    public void VolumeArgs_WithinPAScale()
    {
        string args = SoundHelper.BuildPlayerArguments("paplay", "notify.wav");

        args.Should().Contain("65536");
        args.Should().NotContain("327680");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task WindowsPlay_DoesNotDisposeEarly()
    {
        var order = new List<string>();
        var mock = new Mock<SoundHelper.IWindowsSoundPlayer>();

        mock.Setup(p => p.PlaySync()).Callback(() => order.Add("play"));
        mock.Setup(p => p.Dispose()).Callback(() => order.Add("dispose"));

        Func<string, SoundHelper.IWindowsSoundPlayer>? previous = SoundHelper.WindowsSoundPlayerFactory;
        SoundHelper.WindowsSoundPlayerFactory = _ => mock.Object;

        try
        {
            await SoundHelper.PlayWindowsSoundAsync("notify.wav");
        }
        finally
        {
            SoundHelper.WindowsSoundPlayerFactory = previous;
        }

        order.Should().Equal("play", "dispose");
    }

    public static IEnumerable<object[]> PlayerArgumentCases { get; } = new List<object[]>
    {
        new object[] { "pw-play", "notify.wav", "--volume=1.0 \"notify.wav\"" },
        new object[] { "paplay", "notify.wav", "--volume=65536 \"notify.wav\"" },
        new object[] { "aplay", "notify.wav", "\"notify.wav\"" },
        new object[] { "pw-play", "a\"b.wav", "--volume=1.0 \"a\\\"b.wav\"" },
        new object[] { "paplay", "a\"b.wav", "--volume=65536 \"a\\\"b.wav\"" },
        new object[] { "aplay", "a\"b.wav", "\"a\\\"b.wav\"" },
        new object[] { "pw-play", "a\\b.wav", "--volume=1.0 \"a\\\\b.wav\"" },
    };

    [Theory]
    [MemberData(nameof(PlayerArgumentCases))]
    public void BuildPlayerArguments_Scenario_ExpectedResult(string player, string path, string expected)
    {
        string actual = SoundHelper.BuildPlayerArguments(player, path);

        actual.Should().Be(expected);
    }

    [Fact]
    public void Probe_WhenHelperHangs_FallsThroughWithinTimeout()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), $"ct_probe_{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);

        try
        {
            string hangPath = Path.Combine(dir, "hang-helper");
            string quickPath = Path.Combine(dir, "quick-helper");

            File.WriteAllText(hangPath, "#!/bin/sh\nsleep 30\n");
            File.WriteAllText(quickPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(hangPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(quickPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var sw = Stopwatch.StartNew();

            string? result = SoundHelper.ProbeLinuxPlayer(
                new[] { hangPath, quickPath },
                static psi => Process.Start(psi),
                TimeSpan.FromSeconds(2));

            sw.Stop();

            (result == quickPath && sw.Elapsed < TimeSpan.FromSeconds(15)).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Probe_WhenAllHelpersHang_ReturnsNullWithinTimeout()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), $"ct_probe_{Guid.NewGuid():N}");

        Directory.CreateDirectory(dir);

        try
        {
            string hangOne = Path.Combine(dir, "hang-one");
            string hangTwo = Path.Combine(dir, "hang-two");

            File.WriteAllText(hangOne, "#!/bin/sh\nsleep 30\n");
            File.WriteAllText(hangTwo, "#!/bin/sh\nsleep 30\n");
            File.SetUnixFileMode(hangOne, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(hangTwo, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var sw = Stopwatch.StartNew();

            string? result = SoundHelper.ProbeLinuxPlayer(
                new[] { hangOne, hangTwo },
                static psi => Process.Start(psi),
                TimeSpan.FromSeconds(1));

            sw.Stop();

            (result is null && sw.Elapsed < TimeSpan.FromSeconds(15)).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }
}
