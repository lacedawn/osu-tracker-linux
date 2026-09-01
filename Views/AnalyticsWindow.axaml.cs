using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Circle_Tracker.ViewModels;

namespace Circle_Tracker.Views;

public partial class AnalyticsWindow : Window
{
    public AnalyticsWindow()
    {
        InitializeComponent();
    }

    public AnalyticsWindow(AnalyticsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        _ = viewModel.InitializeAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
