using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public partial class MainWindow : Window, IMainWindow
    {
        private readonly TosuClient _tosuClient;
        private readonly Tracker _tracker;

        private DispatcherTimer? _gameTickTimer;
        private DispatcherTimer? _uiUpdateTimer;
        private DispatcherTimer? _secondsTimer;

        private bool _suppressStartupCheckboxEvent = false;

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
                catch (Exception ex) { Console.Error.WriteLine($"[CircleTracker] Update check failed: {ex.Message}"); }
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
                int playing = _tracker.PlayingSeconds;
                int idle = _tracker.IdleSeconds;
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
            bool playing = _tracker.IsPlaying && _tracker.SheetsApiReady;
            bool valsBad = _tracker.BeatmapStars == 0
                        && _tracker.BeatmapAim == 0
                        && _tracker.BeatmapSpeed == 0
                        && _tracker.BeatmapCs == 0
                        && _tracker.BeatmapAr == 0
                        && _tracker.BeatmapOd == 0;

            ClientDetectedText.Text = _tosuClient.IsConnected ? _tracker.DetectedClient : "-";

            BeatmapInfoGrid.Background = playing
                ? new SolidColorBrush(Color.FromRgb(0x1a, 0x3a, 0x1e))
                : Brushes.Transparent;

            HitsTextBox.Text = $"{_tracker.TotalBeatmapHits} ({_tracker.Play300c}, {_tracker.Play100c}, {_tracker.Play50c}, {_tracker.PlayMissc})";
            TimeTextBox.Text = _tracker.Time.ToString();
            BeatmapTextBox.Text = _tracker.BeatmapString ?? "";
            StarsTextBox.Text = _tracker.BeatmapStars.ToString("0.00");
            AimTextBox.Text = _tracker.BeatmapAim.ToString("0.00");
            SpeedTextBox.Text = _tracker.BeatmapSpeed.ToString("0.00");
            ModsTextBox.Text = _tracker.GetModsString();
            TextBoxCS.Text = _tracker.BeatmapCs.ToString("0.0");
            TextBoxAR.Text = _tracker.BeatmapAr.ToString("0.0");
            TextBoxOD.Text = _tracker.BeatmapOd.ToString("0.0");
            AccTextBox.Text = _tracker.Accuracy.ToString("0.00") + "%";
            BpmTextBox.Text = _tracker.BeatmapBpm.ToString();

            SetReadonlyFieldBad(BeatmapTextBox, string.IsNullOrEmpty(_tracker.BeatmapString));
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

        private void TosuHostTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            string host = TosuHostTextBox.Text?.Trim() ?? "127.0.0.1";
            if (!string.IsNullOrEmpty(host))
            {
                _tracker.TosuHost = host;
                _tosuClient.Host = host;
            }
        }

        private void TosuPortTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TosuPortTextBox.Text?.Trim(), out int port) && port > 0)
            {
                _tracker.TosuPort = port;
                _tosuClient.Port = port;
            }
        }
    }
}
