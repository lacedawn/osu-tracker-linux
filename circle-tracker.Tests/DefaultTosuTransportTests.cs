using Circle_Tracker;
using CircleTracker.Tests.Mocks;
using FluentAssertions;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests;

public class DefaultTosuTransportTests
{
    [Fact]
    public async Task Transport_WhenWebSocketExitsCleanly_ReportsDisconnected()
    {
        using var server = new MockTosuWebSocketServer();
        using var httpClient = new HttpClient();
        using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };
        var transport = new DefaultTosuTransport(client, httpClient);
        using var cts = new CancellationTokenSource();

        Task<bool> sessionTask = transport.RunWebSocketSessionAsync(new byte[65536], cts.Token);
        await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(3));
        await server.CloseAllConnectionsAsync();

        bool result = await sessionTask.WaitAsync(TimeSpan.FromSeconds(5));
        await client.DisconnectAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Transport_WhenSessionCancelled_ReportsDisconnected()
    {
        using var server = new MockTosuWebSocketServer();
        using var httpClient = new HttpClient();
        using var client = new TosuClient { Host = "127.0.0.1", Port = server.Port };
        var transport = new DefaultTosuTransport(client, httpClient);
        using var cts = new CancellationTokenSource();

        Task<bool> sessionTask = transport.RunWebSocketSessionAsync(new byte[65536], cts.Token);
        await server.WaitForClientConnectionAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();

        bool result = await sessionTask.WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().BeFalse();
    }
}
