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
        private static readonly IBrush CyanBrush = new SolidColorBrush(Color.FromRgb(0x38, 0xbd, 0xf8));
        private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xfb, 0x92, 0x3c));
        private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0x93, 0x8b, 0xa8));

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
                CredentialsLabel.Text = found ? "Found" : "Missing";
                CredentialsLabel.Classes.Remove(found ? "status-disconnected" : "status-connected");
                CredentialsLabel.Classes.Add(found ? "status-connected" : "status-disconnected");
            });
        }

        public void SetSheetsApiReady(bool val)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusLabel.Text = val ? "Connected" : "Not connected";
                StatusLabel.Classes.Remove(val ? "status-disconnected" : "status-connected");
                StatusLabel.Classes.Add(val ? "status-connected" : "status-disconnected");

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
                TimeLabelText.Text =
                    $"Playing: {playing}  Idle: {idle}  Efficiency: {(int)eff}%";
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
                    var dlg = new Window
                    {
                        Title = title,
                        Width = 460,
                        Height = 220,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = new StackPanel
                        {
                            Margin = new Avalonia.Thickness(20),
                            Spacing = 16,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = message,
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                                    Foreground = Brushes.White
                                },
                                new Button
                                {
                                    Content = "OK",
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                                    Padding = new Avalonia.Thickness(20, 6)
                                }
                            }
                        }
                    };
                    var btn = ((StackPanel)dlg.Content!).Children[1] as Button;
                    if (btn != null) btn.Click += (_, _) => dlg.Close();

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
                    var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
                    panel.Children.Add(new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Foreground = Brushes.White
                    });
                    var btnRow = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8
                    };
                    var dlg = new Window
                    {
                        Title = title,
                        Width = 460,
                        Height = 200,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Content = panel
                    };
                    var yesBtn = new Button { Content = "Yes", Padding = new Avalonia.Thickness(20, 6) };
                    var noBtn = new Button { Content = "No", Padding = new Avalonia.Thickness(20, 6) };
                    yesBtn.Click += (_, _) => { result = true; dlg.Close(); };
                    noBtn.Click += (_, _) => { result = false; dlg.Close(); };
                    btnRow.Children.Add(yesBtn);
                    btnRow.Children.Add(noBtn);
                    panel.Children.Add(btnRow);

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
            bool playing = s.IsPlaying && s.SheetsApiReady;
            bool valsBad = s.BeatmapStars == 0
                        && s.BeatmapAim == 0
                        && s.BeatmapSpeed == 0
                        && s.BeatmapCs == 0
                        && s.BeatmapAr == 0
                        && s.BeatmapOd == 0;

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

            LoadCoverImage(s.CoverUrl);

            BeatmapInfoGrid.Background = playing
                ? new SolidColorBrush(Color.FromRgb(0x1a, 0x3a, 0x1e))
                : Brushes.Transparent;

            HitsTextBox.Text = $"{s.TotalBeatmapHits} ({s.Play300c}, {s.Play100c}, {s.Play50c}, {s.PlayMissc})";
            TimeTextBox.Text = s.Time.ToString();
            StarsTextBox.Text = s.BeatmapStars.ToString("0.00");
            AimTextBox.Text = s.BeatmapAim.ToString("0.00");
            SpeedTextBox.Text = s.BeatmapSpeed.ToString("0.00");
            ModsTextBox.Text = s.ModsString;
            TextBoxCS.Text = s.BeatmapCs.ToString("0.0");
            TextBoxAR.Text = s.BeatmapAr.ToString("0.0");
            TextBoxOD.Text = s.BeatmapOd.ToString("0.0");
            AccTextBox.Text = s.Accuracy.ToString("0.00") + "%";
            BpmTextBox.Text = s.BeatmapBpm.ToString();

            SetReadonlyFieldBad(StarsTextBox, valsBad);
            SetReadonlyFieldBad(AimTextBox, valsBad);
            SetReadonlyFieldBad(SpeedTextBox, valsBad);
            SetReadonlyFieldBad(TextBoxCS, valsBad);
            SetReadonlyFieldBad(TextBoxAR, valsBad);
            SetReadonlyFieldBad(TextBoxOD, valsBad);
        }

        private static void SetReadonlyFieldBad(TextBox tb, bool bad)
        {
            if (bad) tb.Classes.Add("bad-value");
            else tb.Classes.Remove("bad-value");
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
