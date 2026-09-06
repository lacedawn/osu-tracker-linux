using Circle_Tracker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CircleTracker.Tests.Mocks
{
    public class MockTosuWebSocketServer : IAsyncDisposable, IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts;
        private readonly Task _acceptLoopTask;
        private readonly object _socketsLock = new();
        private readonly List<WebSocket> _connectedSockets = new();

        public int Port { get; }
        public string Url => $"ws://127.0.0.1:{Port}/websocket/v2";
        public bool SimulateAbruptClose { get; set; } = false;

        public MockTosuWebSocketServer()
        {
            Port = GetEphemeralPort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _acceptLoopTask = Task.Run(AcceptLoopAsync);
        }

        private static int GetEphemeralPort()
        {
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                                var ws = wsContext.WebSocket;

                                lock (_socketsLock)
                                {
                                    _connectedSockets.Add(ws);
                                }

                                var buffer = new byte[1024];
                                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                                {
                                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                                    if (result.MessageType == WebSocketMessageType.Close)
                                    {
                                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                            }
                            finally
                            {
                                lock (_socketsLock)
                                {
                                    _connectedSockets.RemoveAll(s => s.State != WebSocketState.Open);
                                }
                            }
                        });
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) when (_cts.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (_cts.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        public async Task WaitForClientConnectionAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                lock (_socketsLock)
                {
                    if (_connectedSockets.Any(s => s.State == WebSocketState.Open))
                    {
                        return;
                    }
                }
                await Task.Delay(25, ct);
            }

            throw new TimeoutException("No WebSocket client connected within timeout.");
        }

        public async Task BroadcastJsonAsync(string json, CancellationToken ct = default)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            List<WebSocket> socketsToBroadcast;
            lock (_socketsLock)
            {
                socketsToBroadcast = _connectedSockets.Where(s => s.State == WebSocketState.Open).ToList();
            }

            foreach (var ws in socketsToBroadcast)
            {
                try
                {
                    await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, ct);
                }
                catch
                {
                }
            }
        }

        public async Task BroadcastPartialJsonAsync(string json, int chunkSize, CancellationToken ct = default)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            List<WebSocket> socketsToBroadcast;
            lock (_socketsLock)
            {
                socketsToBroadcast = _connectedSockets.Where(s => s.State == WebSocketState.Open).ToList();
            }

            foreach (var ws in socketsToBroadcast)
            {
                try
                {
                    int offset = 0;
                    while (offset < payload.Length)
                    {
                        int length = Math.Min(chunkSize, payload.Length - offset);
                        bool isEnd = (offset + length) >= payload.Length;
                        await ws.SendAsync(new ArraySegment<byte>(payload, offset, length), WebSocketMessageType.Text, isEnd, ct);
                        offset += length;
                    }
                }
                catch
                {
                }
            }
        }

        public async Task CloseAllConnectionsAsync()
        {
            List<WebSocket> socketsToClose;
            lock (_socketsLock)
            {
                socketsToClose = _connectedSockets.ToList();
            }

            foreach (var ws in socketsToClose)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
                    }
                }
                catch
                {
                }
            }
        }

        public Task BroadcastStateAsync(TosuState state, CancellationToken ct = default)
        {
            string json = JsonSerializer.Serialize(state, TosuClient.SerializerOptions);
            return BroadcastJsonAsync(json, ct);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
            }

            List<WebSocket> socketsToClose;
            lock (_socketsLock)
            {
                socketsToClose = _connectedSockets.ToList();
                _connectedSockets.Clear();
            }

            foreach (var ws in socketsToClose)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    ws.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                await _acceptLoopTask;
            }
            catch
            {
            }

            _cts.Dispose();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
