using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker;

public class TosuClient : ITosuClient, IDisposable, IAsyncDisposable
{
    private static readonly ILogger<TosuClient> _log = AppLogger.For<TosuClient>();

    private const int BufferSize = 65536;
    private const int HttpPollIntervalMs = 500;
    private const int WsRetryIntervalMs = 5000;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 24050;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ITosuTransport _transport;
    private readonly HttpClient _httpClient;

    private TosuState? _latestState;
    private bool _isConnected;
    private bool _disposed;

    private CancellationTokenSource? _cts;
    private Task? _runnerTask;

    internal Task? RunnerTask => _runnerTask;

    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _isConnected;
            }
        }
        internal set
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
        internal set
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
        _transport = new DefaultTosuTransport(this, _httpClient);
    }

    public TosuClient(ITosuTransport transport, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        _transport = transport;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TosuClient));

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runnerTask != null && !_runnerTask.IsCompleted)
                return;

            _cts = new CancellationTokenSource();
            _runnerTask = Task.Run(() => ConnectionLoopAsync(_cts.Token), CancellationToken.None);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (_runnerTask != null)
                {
                    try
                    {
                        await _runnerTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                }
                _cts.Dispose();
                _cts = null;
            }

            IsConnected = false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task ReconnectAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await ConnectAsync().ConfigureAwait(false);
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
                bool wsSuccess = await _transport.RunWebSocketSessionAsync(buffer, ct).ConfigureAwait(false);
                if (wsSuccess || ct.IsCancellationRequested)
                    continue;
            }

            var state = await _transport.PollHttpSnapshotAsync(ct).ConfigureAwait(false);
            if (state != null)
            {
                LatestState = state;
                IsConnected = true;
            }
            else
            {
                IsConnected = false;
            }

            try
            {
                await Task.Delay(HttpPollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        IsConnected = false;
    }

    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PpCalcResult?> CalculatePpAsync(int modNumber = 0, CancellationToken ct = default)
    {
        try
        {
            string url = modNumber != 0
                ? $"http://{Host}:{Port}/api/calculate/pp?mods={modNumber}"
                : $"http://{Host}:{Port}/api/calculate/pp";

            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<PpCalcResult>(json, SerializerOptions);
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

        _connectLock.Dispose();
        _httpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
            if (_runnerTask != null)
            {
                try
                {
                    await _runnerTask.ConfigureAwait(false);
                }
                catch { }
            }
            _cts?.Dispose();
        }
        catch { }

        _connectLock.Dispose();
        _httpClient.Dispose();
    }
}
