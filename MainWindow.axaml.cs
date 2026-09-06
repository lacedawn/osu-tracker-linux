using Avalonia.Controls;
using Avalonia.Interactivity;
using Circle_Tracker.ViewModels;
using Circle_Tracker.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public partial class MainWindow : Window
    {
        private static readonly ILogger<MainWindow> _log = AppLogger.For<MainWindow>();
        private readonly MainWindowViewModel? _viewModel;
        private readonly CancellationTokenSource _updateCheckCts = new();
        private bool _isExplicitShutdownComplete;

        public MainWindow() : this(null!)
        {
        }

        public MainWindow(MainWindowViewModel viewModel)
        {
            string? exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (exeDir != null)
            {
                Directory.SetCurrentDirectory(exeDir);
            }

            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            if (_viewModel != null)
            {
                _viewModel.OpenAnalyticsRequested += OpenAnalyticsWindow;
                _viewModel.SessionSummaryRequested += ShowSessionSummaryDialogAsync;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Updater.CheckForUpdates();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Update check failed");
                }
            }, _updateCheckCts.Token);
        }

        private void OpenAnalyticsWindow()
        {
            try
            {
                var serviceProvider = Program.GetServiceProvider();
                var analyticsVm = serviceProvider?.GetService<AnalyticsViewModel>()
                    ?? _viewModel?.CreateAnalyticsViewModel();

                if (analyticsVm != null)
                {
                    var analyticsWindow = new AnalyticsWindow(analyticsVm);
                    analyticsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to open analytics window");
            }
        }

        private async Task<bool> ShowSessionSummaryDialogAsync()
        {
            if (_viewModel == null) return false;
            try
            {
                var summary = await _viewModel.GenerateSessionSummaryAsync();
                var dialog = new SessionSummaryDialog(summary);
                await dialog.ShowDialog(this);
                if (dialog.ShouldOpenAnalytics)
                {
                    OpenAnalyticsWindow();
                }
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to show session summary dialog");
                return false;
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            if (_isExplicitShutdownComplete)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            _updateCheckCts.Cancel();

            try
            {
                IsEnabled = false;
                if (_viewModel != null && _viewModel.SessionLive.LiveSessionCardVisible)
                {
                    await ShowSessionSummaryDialogAsync();
                }

                if (_viewModel != null)
                {
                    await _viewModel.ShutdownAsync();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error during application shutdown");
            }
            finally
            {
                _isExplicitShutdownComplete = true;
                Close();
            }
        }

        private void SettingsToggleButton_Click(object? sender, RoutedEventArgs e)
        {
            SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
            if (SettingsPanel.IsVisible)
            {
                SettingsToggleText.Text = "▼ Less";
                if (Height < 560) Height = 560;
            }
            else
            {
                SettingsToggleText.Text = "► More!";
                Height = 400;
            }
        }
    }
}
