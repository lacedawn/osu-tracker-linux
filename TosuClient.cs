using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public class TosuClient : ITosuClient, IDisposable
    {
        private static readonly ILogger<TosuClient> _log = AppLogger.For<TosuClient>();

        private const int BufferSize = 65536;
        private const int HttpPollIntervalMs = 500;
        private const int WsRetryIntervalMs = 5000;

        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 24050;

        private readonly object _stateLock = new();
        private TosuState? _latestState;
        private bool _isConnected;
        private bool _disposed;

        private CancellationTokenSource? _cts;
        private Task? _runnerTask;
        private readonly HttpClient _httpClient;

        public bool IsConnected
        {
            get
            {
                lock (_stateLock)
                {
                    return _isConnected;
                }
            }
            private set
            {
                bool changed = false;
                lock (_stateLock)
                {
                    if (_isConnected != value)
                    {
                        _isConnected = value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    ConnectionStateChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<bool>? ConnectionStateChanged;

        public TosuState? LatestState
        {
            get
            {
                lock (_stateLock)
                {
                    return _latestState;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _latestState = value;
                }

                if (value != null)
                {
                    StateUpdated?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<TosuState>? StateUpdated;

        public TosuClient()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TosuClient));

            if (_runnerTask != null && !_runnerTask.IsCompleted)
                return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            _runnerTask = Task.Run(() => ConnectionLoopAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (_runnerTask != null)
                {
                    try
                    {
                        await _runnerTask;
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                }
                _cts.Dispose();
                _cts = null;
            }

            IsConnected = false;
        }

        public async Task ReconnectAsync()
        {
            await DisconnectAsync();
            await ConnectAsync();
        }

        private async Task ConnectionLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[BufferSize];
            DateTime lastWsAttempt = DateTime.MinValue;

            while (!ct.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - lastWsAttempt).TotalMilliseconds >= WsRetryIntervalMs)
                {
                    lastWsAttempt = DateTime.UtcNow;
                    bool wsSuccess = await RunWebSocketSessionAsync(buffer, ct);
                    if (wsSuccess || ct.IsCancellationRequested)
                        continue;
                }

                await PollHttpSnapshotAsync(ct);

                try
                {
                    await Task.Delay(HttpPollIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            IsConnected = false;
        }

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Error = (_, args) => { args.ErrorContext.Handled = true; }
        };

        private async Task<bool> RunWebSocketSessionAsync(byte[] buffer, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            Uri wsUri = new Uri($"ws://{Host}:{Port}/websocket/v2");

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(3));
                await ws.ConnectAsync(wsUri, connectCts.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning("WebSocket connect failed: {ErrorType}", ex.GetType().Name);
                return false;
            }

            if (ws.State != WebSocketState.Open)
                return false;

            IsConnected = true;

            using var ms = new MemoryStream();
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            IsConnected = false;
                            return false;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        ms.Position = 0;
                        using var reader = new StreamReader(ms, Encoding.UTF8, false, 1024, leaveOpen: true);
                        string json = await reader.ReadToEndAsync();

                        try
                        {
                            var state = JsonConvert.DeserializeObject<TosuState>(json, SerializerSettings);
                            if (state != null)
                            {
                                LatestState = state;
                                IsConnected = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Failed to deserialize WebSocket message");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WebSocket session error");
            }
            finally
            {
                IsConnected = false;
            }

            return true;
        }

        private async Task PollHttpSnapshotAsync(CancellationToken ct)
        {
            try
            {
                string url = $"http://{Host}:{Port}/json/v2";
                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    var state = JsonConvert.DeserializeObject<TosuState>(json, SerializerSettings);
                    if (state != null)
                    {
                        LatestState = state;
                        IsConnected = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug("HTTP poll failed: {ErrorType}", ex.GetType().Name);
            }

            IsConnected = false;
        }

        public async Task<PpCalcResult?> CalculatePpAsync(int modNumber = 0, CancellationToken ct = default)
        {
            try
            {
                string url = modNumber != 0
                    ? $"http://{Host}:{Port}/api/calculate/pp?mods={modNumber}"
                    : $"http://{Host}:{Port}/api/calculate/pp";

                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(ct);
                    return JsonConvert.DeserializeObject<PpCalcResult>(json);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PP calculation request failed");
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }

            _httpClient.Dispose();
        }
    }
}
