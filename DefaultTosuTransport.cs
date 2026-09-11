using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker;

public class DefaultTosuTransport : ITosuTransport
{
    private static readonly ILogger<DefaultTosuTransport> _log = AppLogger.For<DefaultTosuTransport>();
    private readonly TosuClient _client;
    private readonly HttpClient _httpClient;

    public DefaultTosuTransport(TosuClient client, HttpClient httpClient)
    {
        _client = client;
        _httpClient = httpClient;
    }

    public async Task<bool> RunWebSocketSessionAsync(byte[] buffer, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        var wsUri = new Uri($"ws://{_client.Host}:{_client.Port}/websocket/v2");

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(wsUri, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning("WebSocket connect failed: {ErrorType}", ex.GetType().Name);
            return false;
        }

        if (ws.State != WebSocketState.Open)
            return false;

        _client.IsConnected = true;

        using var ms = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                        try
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (WebSocketException)
                        {
                        }

                        _client.IsConnected = false;
                        return false;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    ms.Position = 0;

                    try
                    {
                        var state = JsonSerializer.Deserialize<TosuState>(ms, TosuClient.SerializerOptions);
                        if (state != null)
                        {
                            _client.LatestState = state;
                            _client.IsConnected = true;
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
            _client.IsConnected = false;
        }

        return true;
    }

    public async Task<TosuState?> PollHttpSnapshotAsync(CancellationToken ct)
    {
        try
        {
            string url = $"http://{_client.Host}:{_client.Port}/json/v2";
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var state = await JsonSerializer.DeserializeAsync<TosuState>(stream, TosuClient.SerializerOptions, ct).ConfigureAwait(false);
                return state;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("HTTP poll failed: {ErrorType}", ex.GetType().Name);
        }

        return null;
    }
}
