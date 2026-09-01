using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Circle_Tracker.Services;

namespace Circle_Tracker.Views;

public partial class SessionSummaryDialog : Window
{
    public bool ShouldOpenAnalytics { get; private set; }

    public SessionSummaryDialog()
    {
        InitializeComponent();
    }

    public SessionSummaryDialog(SessionSummaryReport report) : this()
    {
        DataContext = report;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ViewAnalytics_Click(object? sender, RoutedEventArgs e)
    {
        ShouldOpenAnalytics = true;
        Close();
    }

    private void CloseApp_Click(object? sender, RoutedEventArgs e)
    {
        ShouldOpenAnalytics = false;
        Close();
    }
}
