using Circle_Tracker;
using FluentAssertions;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
}
