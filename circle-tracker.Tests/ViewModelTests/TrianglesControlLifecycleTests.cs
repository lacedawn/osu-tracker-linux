using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Circle_Tracker;
using FluentAssertions;
using System;
using Xunit;

namespace CircleTracker.Tests.ViewModelTests;

public class TrianglesControlLifecycleTests
{
    [AvaloniaFact]
    public void WindowState_ChangedToMinimized_ImmediatelyStopsDispatcherTimer()
    {
        var window = new Window();
        var control = new TrianglesControl();
        window.Content = control;
        window.Show();

        control.IsTimerRunning.Should().BeTrue();

        window.WindowState = WindowState.Minimized;

        control.IsTimerRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void WindowState_RestoredFromMinimized_ResumesAnimation()
    {
        var window = new Window();
        var control = new TrianglesControl();
        window.Content = control;
        window.WindowState = WindowState.Minimized;
        window.Show();

        control.IsTimerRunning.Should().BeFalse();

        window.WindowState = WindowState.Normal;

        control.IsTimerRunning.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Control_DetachedFromVisualTree_DisposesTimerSubscriptions()
    {
        var window = new Window();
        var control = new TrianglesControl();
        window.Content = control;
        window.Show();

        control.IsTimerRunning.Should().BeTrue();

        window.Content = null;

        control.IsTimerRunning.Should().BeFalse();

        window.WindowState = WindowState.Minimized;
        window.WindowState = WindowState.Normal;

        control.IsTimerRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Window_Deactivated_ThrottlesTimerIntervalTo100Ms()
    {
        var window = new Window();
        var control = new TrianglesControl { DisableBackgroundAnimationsWhenUnfocused = false };
        window.Content = control;
        window.Show();

        control.IsTimerRunning.Should().BeTrue();
        control.TimerInterval.Should().Be(TimeSpan.FromMilliseconds(16));

        control.HandleWindowDeactivated();

        control.IsTimerRunning.Should().BeTrue();
        control.TimerInterval.Should().Be(TimeSpan.FromMilliseconds(100));

        control.HandleWindowActivated();

        control.IsTimerRunning.Should().BeTrue();
        control.TimerInterval.Should().Be(TimeSpan.FromMilliseconds(16));
    }

    [AvaloniaFact]
    public void Window_DeactivatedWithDisableBackgroundAnimations_StopsTimer()
    {
        var window = new Window();
        var control = new TrianglesControl { DisableBackgroundAnimationsWhenUnfocused = true };
        window.Content = control;
        window.Show();

        control.IsTimerRunning.Should().BeTrue();

        control.HandleWindowDeactivated();

        control.IsTimerRunning.Should().BeFalse();

        control.HandleWindowActivated();

        control.IsTimerRunning.Should().BeTrue();
        control.TimerInterval.Should().Be(TimeSpan.FromMilliseconds(16));
    }
}
