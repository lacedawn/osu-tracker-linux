using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
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

        private static readonly IBrush GreenBrush = new SolidColorBrush(Color.FromRgb(0x55, 0xcc, 0x77));
        private static readonly IBrush RedBrush = new SolidColorBrush(Color.FromRgb(0xcc, 0x55, 0x55));

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

            _tracker.InitGoogleAPI(silent: true);
            SetCredentialsFound(File.Exists(Path.Combine(AppContext.BaseDirectory, "credentials.json")));

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
            if (!connected)
            {
                ClientDetectedText.Text = "-";
            }
        }

        public void SetCredentialsFound(bool found)
        {
            CredentialsLabel.Text = found ? "Found" : "Missing";
            CredentialsLabel.Classes.Remove(found ? "status-disconnected" : "status-connected");
            CredentialsLabel.Classes.Add(found ? "status-connected" : "status-disconnected");
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
                await dlg.ShowDialog(this);
            });
        }

        public async Task<bool> ShowYesNoDialog(string message, string title = "Confirm")
        {
            var tcs = new TaskCompletionSource<bool>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
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
                await dlg.ShowDialog(this);
                tcs.SetResult(result);
            });
            return await tcs.Task;
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

            ClientDetectedText.Text = _tosuClient.IsConnected ? s.DetectedClient : "-";

            BeatmapInfoGrid.Background = playing
                ? new SolidColorBrush(Color.FromRgb(0x1a, 0x3a, 0x1e))
                : Brushes.Transparent;

            HitsTextBox.Text = $"{s.TotalBeatmapHits} ({s.Play300c}, {s.Play100c}, {s.Play50c}, {s.PlayMissc})";
            TimeTextBox.Text = s.Time.ToString();
            BeatmapTextBox.Text = s.BeatmapString;
            StarsTextBox.Text = s.BeatmapStars.ToString("0.00");
            AimTextBox.Text = s.BeatmapAim.ToString("0.00");
            SpeedTextBox.Text = s.BeatmapSpeed.ToString("0.00");
            ModsTextBox.Text = s.ModsString;
            TextBoxCS.Text = s.BeatmapCs.ToString("0.0");
            TextBoxAR.Text = s.BeatmapAr.ToString("0.0");
            TextBoxOD.Text = s.BeatmapOd.ToString("0.0");
            AccTextBox.Text = s.Accuracy.ToString("0.00") + "%";
            BpmTextBox.Text = s.BeatmapBpm.ToString();

            SetReadonlyFieldBad(BeatmapTextBox, string.IsNullOrEmpty(s.BeatmapString));
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
            _tracker.InitGoogleAPI();
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
