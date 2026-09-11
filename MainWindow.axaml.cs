using Avalonia.Controls;
using Avalonia.Interactivity;
using Circle_Tracker.ViewModels;
using Circle_Tracker.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public partial class MainWindow : Window
    {
        private static readonly ILogger<MainWindow> _log = AppLogger.For<MainWindow>();
        private const string ShowLessText = "▼ Less";
        private const string ShowMoreText = "► More!";
        private readonly MainWindowViewModel? _viewModel;
        private readonly CancellationTokenSource _updateCheckCts = new();
        private bool _isExplicitShutdownComplete;

        public MainWindow() : this(null!)
        {
        }

        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            if (_viewModel != null)
            {
                _viewModel.OpenAnalyticsRequested += OpenAnalyticsWindow;
                _viewModel.SessionSummaryRequested += ShowSessionSummaryDialogAsync;
                _viewModel.ShowYesNoDialogDelegate = ShowRealYesNoDialogAsync;
                _viewModel.ShowMessageDelegate = ShowRealMessageAsync;
            }

            CancellationToken updateToken = _updateCheckCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Updater.CheckForUpdates(ct: updateToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Update check failed");
                }
            }, CancellationToken.None);
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

        private async Task<bool> ShowRealYesNoDialogAsync(string message, string title)
        {
            try
            {
                var dialog = new ConfirmDialog(message, title);
                return await dialog.ShowDialog<bool>(this);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to show confirmation dialog");
                return false;
            }
        }

        private async Task ShowRealMessageAsync(string message, string title)
        {
            try
            {
                var dialog = new ConfirmDialog(message, title);
                await dialog.ShowDialog<bool>(this);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to show message dialog");
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
            if (_viewModel == null) return;
            _viewModel.IsSettingsPanelVisible = !_viewModel.IsSettingsPanelVisible;
            SettingsToggleText.Text = _viewModel.IsSettingsPanelVisible ? ShowLessText : ShowMoreText;
            SizeToContent = SizeToContent.Height;
        }
    }
}
