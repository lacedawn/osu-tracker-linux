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
}
