using Circle_Tracker;
using FluentAssertions;
using System;
using Xunit;

namespace CircleTracker.Tests.PlatformTests;

public class AutostartHelperTests : IDisposable
{
    private readonly string? _originalFlatpakId;

    public AutostartHelperTests()
    {
        _originalFlatpakId = Environment.GetEnvironmentVariable("FLATPAK_ID");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FLATPAK_ID", _originalFlatpakId);
    }

    [Fact]
    public void GetExecutablePath_UnderFlatpakEnvironment_ReturnsFlatpakRunCommand()
    {
        string flatpakId = "org.lacedawn.CircleTracker";
        Environment.SetEnvironmentVariable("FLATPAK_ID", flatpakId);

        string desktopEntry = AutostartHelper.GenerateDesktopEntry();

        desktopEntry.Should().Contain($"Exec=flatpak run {flatpakId}");
    }

    [Fact]
    public void FormatExec_WhenPathHasSpaces_QuotesCorrectly()
    {
        string pathWithSpaces = "/opt/Circle Tracker/circle-tracker";

        string formatted = AutostartHelper.FormatExec(pathWithSpaces);

        formatted.Should().Be("\"/opt/Circle Tracker/circle-tracker\"");
    }

    [Fact]
    public void FormatExec_WhenAlreadyQuoted_DoesNotDoubleQuote()
    {
        string alreadyQuoted = "\"/opt/Circle Tracker/circle-tracker\"";

        string formatted = AutostartHelper.FormatExec(alreadyQuoted);

        formatted.Should().Be("\"/opt/Circle Tracker/circle-tracker\"");
    }

    [Fact]
    public void GenerateDesktopEntry_ContainsRequiredDesktopHeaders()
    {
        string entry = AutostartHelper.GenerateDesktopEntry("/usr/bin/circle-tracker");

        entry.Should().Contain("[Desktop Entry]");
        entry.Should().Contain("Type=Application");
        entry.Should().Contain("Name=Circle Tracker");
        entry.Should().Contain("Exec=/usr/bin/circle-tracker");
        entry.Should().Contain("Icon=circle-tracker");
    }

    [Fact]
    public void GeneratedDesktop_ContainsTerminalFalse()
    {
        string entry = AutostartHelper.GenerateDesktopEntry("/usr/bin/circle-tracker");

        entry.Should().Contain("Terminal=false");
    }
}
