using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public partial class MainWindow : Window, IMainWindow
    {
        private static readonly ILogger<MainWindow> _log = AppLogger.For<MainWindow>();

        private readonly TosuClient _tosuClient;
        private readonly Tracker _tracker;

        private DispatcherTimer? _gameTickTimer;
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _secondsTimer;

        private bool _suppressStartupCheckboxEvent = false;
        private CancellationTokenSource? _reconnectDebounce;

        private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80));
        private static readonly IBrush RedBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
        private static readonly IBrush CyanBrush = new SolidColorBrush(Color.FromRgb(0x7d, 0xd3, 0xfc));
        private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xfb, 0x92, 0x3c));
        private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x8f, 0x87, 0xa3));

        private static readonly HttpClient _imageHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly ConcurrentDictionary<string, Bitmap> _coverCache = new();
        private string _currentCoverUrl = "";
        private CancellationTokenSource? _coverLoadCts;

        public MainWindow()
        {
            string? exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (exeDir != null)
                Directory.SetCurrentDirectory(exeDir);

            InitializeComponent();

            _suppressStartupCheckboxEvent = true;
            StartupCheckBox.IsChecked = AutostartHelper.AutostartExists();
            _suppressStartupCheckboxEvent = false;
            if (AutostartHelper.AutostartExists())
            {
                AutostartHelper.DeleteAutostart();
                AutostartHelper.CreateAutostart();
            }

            _ = Task.Run(async () =>
            {
                try { await Updater.CheckForUpdates(); }
                catch (Exception ex) { _log.LogError(ex, "Update check failed"); }
            });

            _tosuClient = new TosuClient();
            _tracker = new Tracker(this, _tosuClient);

            SheetNameTextBox.Text = _tracker.SheetName;
            SpreadsheetIdTextBox.Text = _tracker.SpreadsheetId;
            SoundEnabledCheckbox.IsChecked = _tracker.SubmitSoundEnabled;
            AltSepCheckBox.IsChecked = _tracker.UseAltFuncSeparator;

            TosuHostTextBox.Text = _tracker.TosuHost;
            TosuPortTextBox.Text = _tracker.TosuPort.ToString();

            _ = _tosuClient.ConnectAsync();

            _tosuClient.ConnectionStateChanged += (s, connected) =>
            {
                Dispatcher.UIThread.Post(() => UpdateTosuStatus(connected));
            };

            SetCredentialsFound(File.Exists(Path.Combine(AppContext.BaseDirectory, "credentials.json")));

            _ = Task.Run(() =>
            {
                try { _tracker.InitGoogleAPI(silent: true); }
                catch (Exception ex) { _log.LogError(ex, "Google API init failed"); }
            });

            SetupTimers();
        }

        private void SetupTimers()
        {
            _gameTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gameTickTimer.Tick += async (s, e) =>
            {
                try
                {
                    await Task.Run(() => _tracker.TickWrapper());
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _gameTickTimer?.Stop();
                    string logPath = Path.Combine(AppContext.BaseDirectory, "errorlog.txt");
                    await File.AppendAllTextAsync(logPath,
                        $"-------------------\n{DateTime.Now}\n-------------------\n{ex}\n\n");
                    ShowMessage(
                        $"An exception occurred:\n\n{ex.Message}\n\nDetails written to errorlog.txt",
                        "Error");
                    _gameTickTimer?.Start();
                }
            };

            _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _uiUpdateTimer.Tick += (s, e) => UpdateControls();

            _secondsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _secondsTimer.Tick += (s, e) => _tracker.TickEverySecond();

            _gameTickTimer.Start();
            _uiUpdateTimer.Start();
            _secondsTimer.Start();
        }

        private void UpdateTosuStatus(bool connected)
        {
            TosuStatusDot.Fill = connected ? GreenBrush : RedBrush;
            TosuStatusText.Text = connected ? "tosu: Connected" : "tosu: Connecting...";
        }

        public void SetCredentialsFound(bool found)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CredentialsLabel != null)
                {
                    CredentialsLabel.Text = found ? "Found" : "Missing";
                    CredentialsLabel.Foreground = found ? GreenBrush : RedBrush;
                }
            });
        }

        public void SetSheetsApiReady(bool val)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SheetsStatusDot.Fill = val ? GreenBrush : RedBrush;
                SheetsStatusText.Text = val ? "Sheets: Connected" : "Sheets: Not connected";
            });
        }

        public void UpdateTime()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var s = _tracker.GetSnapshot();
                int playing = s.PlayingSeconds;
                int idle = s.IdleSeconds;
                float total = playing + idle;
                float eff = total > 0 ? 100f * playing / total : 0f;
                int playingMin = playing / 60;
                int idleMin = idle / 60;
                if (SessionTimeText != null)
                    SessionTimeText.Text = $"Playing: {playingMin}m  Idle: {idleMin}m ({(int)eff}%)";
                if (TimeLabelText != null)
                    TimeLabelText.Text = $"Playing: {playing}s  Idle: {idle}s  Efficiency: {(int)eff}%";
            });
        }

        public void StopUpdateTimer()
        {
            Dispatcher.UIThread.Post(() => _gameTickTimer?.Stop());
        }

        public void ShowMessage(string message, string title = "Info")
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var panel = new StackPanel
                    {
                        Spacing = 16
                    };

                    panel.Children.Add(new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0xf4, 0xfa)),
                        FontSize = 13
                    });

                    var okBtn = new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Classes = { "primary-action" }
                    };

                    panel.Children.Add(okBtn);

                    var border = new Border
                    {
                        Classes = { "hud-card" },
                        Margin = new Thickness(12),
                        Padding = new Thickness(16),
                        Child = panel
                    };

                    var dlg = new Window
                    {
                        Title = title,
                        Width = 440,
                        Height = 200,
                        CanResize = false,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x1d)),
                        Content = border
                    };

                    okBtn.Click += (_, _) => dlg.Close();

                    if (this.IsLoaded && this.IsVisible)
                        await dlg.ShowDialog(this);
                    else
                        dlg.Show();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to display message: {Message}", message);
                }
            });
        }

        public async Task<bool> ShowYesNoDialog(string message, string title = "Confirm")
        {
            var tcs = new TaskCompletionSource<bool>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    bool result = false;
                    var panel = new StackPanel
                    {
                        Spacing = 16
                    };

                    panel.Children.Add(new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0xf4, 0xfa)),
                        FontSize = 13
                    });

                    var btnRow = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8
                    };

                    var yesBtn = new Button
                    {
                        Content = "Yes",
                        Classes = { "primary-action" }
                    };

                    var noBtn = new Button
                    {
                        Content = "No",
                        Classes = { "secondary-flat" }
                    };

                    btnRow.Children.Add(yesBtn);
                    btnRow.Children.Add(noBtn);
                    panel.Children.Add(btnRow);

                    var border = new Border
                    {
                        Classes = { "hud-card" },
                        Margin = new Thickness(12),
                        Padding = new Thickness(16),
                        Child = panel
                    };

                    Window dlg = null!;
                    dlg = new Window
                    {
                        Title = title,
                        Width = 440,
                        Height = 200,
                        CanResize = false,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x1d)),
                        Content = border
                    };

                    yesBtn.Click += (_, _) => { result = true; dlg.Close(); };
                    noBtn.Click += (_, _) => { result = false; dlg.Close(); };

                    if (this.IsLoaded && this.IsVisible)
                        await dlg.ShowDialog(this);
                    else
                        dlg.Show();

                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to display confirm dialog: {Message}", message);
                    tcs.SetResult(false);
                }
            });
            return await tcs.Task;
        }

        private void LoadCoverImage(string coverUrl)
        {
            if (_currentCoverUrl == coverUrl)
                return;

            _currentCoverUrl = coverUrl;
            _coverLoadCts?.Cancel();

            if (string.IsNullOrEmpty(coverUrl))
            {
                CoverImage.Source = null;
                return;
            }

            if (_coverCache.TryGetValue(coverUrl, out var cached))
            {
                CoverImage.Source = cached;
                return;
            }

            _coverLoadCts = new CancellationTokenSource();
            var token = _coverLoadCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] data = await _imageHttpClient.GetByteArrayAsync(coverUrl, token);
                    if (token.IsCancellationRequested) return;

                    using var ms = new MemoryStream(data);
                    var bitmap = new Bitmap(ms);
                    _coverCache[coverUrl] = bitmap;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_currentCoverUrl == coverUrl)
                            CoverImage.Source = bitmap;
                    });
                }
                catch (Exception ex)
                {
                    _log.LogDebug("Failed to load cover image: {Error}", ex.Message);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_currentCoverUrl == coverUrl)
                            CoverImage.Source = null;
                    });
                }
            }, token);
        }

        private void UpdateControls()
        {
            var s = _tracker.GetSnapshot();

            TosuStatusDot.Fill = _tosuClient.IsConnected ? GreenBrush : RedBrush;
            TosuStatusText.Text = _tosuClient.IsConnected ? $"tosu: {s.DetectedClient}" : "tosu: Connecting...";

            GameStateBadge.Text = s.GameStateLabel;
            GameStateBadge.Foreground = s.GameStateLabel switch
            {
                "PLAYING" => GreenBrush,
                "RESULTS" => CyanBrush,
                "REPLAY" => OrangeBrush,
                _ => MutedBrush
            };

            BeatmapTitleText.Text = !string.IsNullOrEmpty(s.BeatmapTitle) ? s.BeatmapTitle : (!string.IsNullOrEmpty(s.BeatmapString) ? s.BeatmapString : "No beatmap detected");
            BeatmapArtistText.Text = !string.IsNullOrEmpty(s.BeatmapArtist) ? s.BeatmapArtist : "-";
            BeatmapVersionText.Text = !string.IsNullOrEmpty(s.BeatmapVersion) ? s.BeatmapVersion : "-";
            BeatmapStarsBadge.Text = $"★ {s.BeatmapStars:0.00}";

            ToolTip.SetTip(BeatmapTitleText, BeatmapTitleText.Text);
            ToolTip.SetTip(BeatmapArtistText, BeatmapArtistText.Text);

            LoadCoverImage(s.CoverUrl);

            StatCsText.Text = s.BeatmapCs.ToString("0.0");
            StatArText.Text = s.BeatmapAr.ToString("0.0");
            StatOdText.Text = s.BeatmapOd.ToString("0.0");
            StatHpText.Text = s.BeatmapHp.ToString("0.0");
            StatBpmText.Text = s.BeatmapBpm.ToString();
            StatModsText.Text = !string.IsNullOrEmpty(s.ModsString) ? $"+{s.ModsString}" : "None";

            Hits300Text.Text = s.Play300c.ToString();
            Hits100Text.Text = s.Play100c.ToString();
            Hits50Text.Text = s.Play50c.ToString();
            HitsMissText.Text = s.PlayMissc.ToString();
            TotalObjectsText.Text = $"Total: {s.TotalBeatmapHits}";

            AccuracyText.Text = $"{s.Accuracy:0.00}%";
            PlayCountBadge.Text = $"Play #{s.PlayCount}";

            int playing = s.PlayingSeconds;
            int idle = s.IdleSeconds;
            float total = playing + idle;
            float eff = total > 0 ? 100f * playing / total : 0f;
            int playingMin = playing / 60;
            int idleMin = idle / 60;
            SessionTimeText.Text = $"Playing: {playingMin}m  Idle: {idleMin}m ({(int)eff}%)";
            TimeLabelText.Text = $"Playing: {playing}s  Idle: {idle}s  Efficiency: {(int)eff}%";
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _tracker.SaveSettings();
            _tosuClient.Dispose();
            _gameTickTimer?.Stop();
            _uiUpdateTimer?.Stop();
            _secondsTimer?.Stop();
            base.OnClosing(e);
        }

        private void SpreadsheetIdTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _tracker.SpreadsheetId = SpreadsheetIdTextBox.Text ?? "";
        }

        private void SheetNameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _tracker.SheetName = SheetNameTextBox.Text ?? "";
        }

        private void SoundEnabledCheckbox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            _tracker.SubmitSoundEnabled = SoundEnabledCheckbox.IsChecked == true;
        }

        private void AltSepCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            _tracker.UseAltFuncSeparator = AltSepCheckBox.IsChecked == true;
        }

        private void ConnectApiButton_Click(object? sender, RoutedEventArgs e)
        {
            _ = Task.Run(() =>
            {
                try { _tracker.InitGoogleAPI(); }
                catch (Exception ex) { _log.LogError(ex, "Google API init failed"); }
            });
        }

        private void SettingsToggleButton_Click(object? sender, RoutedEventArgs e)
        {
            SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
            if (SettingsPanel.IsVisible)
            {
                SettingsToggleText.Text = "▲ Hide Settings";
                if (Height < 560)
                    Height = 560;
            }
            else
            {
                SettingsToggleText.Text = "▼ Settings & Google Sheets";
                Height = 400;
            }
        }

        private void StartupCheckBox_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_suppressStartupCheckboxEvent) return;
            if (StartupCheckBox.IsChecked == true)
                AutostartHelper.CreateAutostart();
            else
                AutostartHelper.DeleteAutostart();
        }

        private void DebounceReconnect()
        {
            _reconnectDebounce?.Cancel();
            _reconnectDebounce = new CancellationTokenSource();
            var token = _reconnectDebounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (!token.IsCancellationRequested)
                        await _tosuClient.ReconnectAsync();
                }
                catch (OperationCanceledException) { }
            });
        }

        private void TosuHostTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            string host = TosuHostTextBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(host) && !host.Contains(' '))
            {
                _tracker.TosuHost = host;
                _tosuClient.Host = host;
                TosuHostTextBox.Classes.Remove("bad-value");
                DebounceReconnect();
            }
            else if (!string.IsNullOrEmpty(TosuHostTextBox.Text))
            {
                TosuHostTextBox.Classes.Add("bad-value");
            }
        }

        private void TosuPortTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            string text = TosuPortTextBox.Text?.Trim() ?? "";
            if (int.TryParse(text, out int port) && port >= 1 && port <= 65535)
            {
                _tracker.TosuPort = port;
                _tosuClient.Port = port;
                TosuPortTextBox.Classes.Remove("bad-value");
                DebounceReconnect();
            }
            else if (!string.IsNullOrEmpty(text))
            {
                TosuPortTextBox.Classes.Add("bad-value");
            }
        }
    }
}
