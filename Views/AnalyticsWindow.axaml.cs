using System;
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
        viewModel.RequestSaveFilePathAsync = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return null;
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Plays to CSV",
                DefaultExtension = "csv",
                SuggestedFileName = $"circle_tracker_plays_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            });
            return file?.Path.LocalPath;
        };
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
