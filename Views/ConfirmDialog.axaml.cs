using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Circle_Tracker.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string message, string title) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Yes_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void No_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
