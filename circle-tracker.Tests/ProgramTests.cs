using Circle_Tracker;
using FluentAssertions;
using Xunit;

namespace CircleTracker.Tests;

public class ProgramTests
{
    [Fact]
    public void TryHandleHeadlessArgs_Help_ReturnsTrue()
    {
        bool handled = Program.TryHandleHeadlessArgs(new[] { "--help" }, out int exit);

        handled.Should().BeTrue();
    }

    [Fact]
    public void TryHandleHeadlessArgs_Help_ExitZero()
    {
        Program.TryHandleHeadlessArgs(new[] { "--help" }, out int exit);

        exit.Should().Be(0);
    }

    [Fact]
    public void TryHandleHeadlessArgs_Version_ReturnsTrue()
    {
        bool handled = Program.TryHandleHeadlessArgs(new[] { "--version" }, out int exit);

        handled.Should().BeTrue();
    }

    [Fact]
    public void TryHandleHeadlessArgs_UnknownArg_ReturnsFalse()
    {
        bool handled = Program.TryHandleHeadlessArgs(new[] { "--run" }, out int exit);

        handled.Should().BeFalse();
    }

    [Fact]
    public void GetAppVersion_NormalRun_ReturnsNonEmpty()
    {
        string version = Program.GetAppVersion();

        version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RunSmokeTest_NormalRun_ReturnsZero()
    {
        int exit = Program.RunSmokeTest();

        exit.Should().Be(0);
    }
}
