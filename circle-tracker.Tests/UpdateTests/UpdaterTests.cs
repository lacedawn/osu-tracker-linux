using Circle_Tracker;
using FluentAssertions;
using Moq;
using Moq.Protected;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CircleTracker.Tests.UpdateTests;

public class UpdaterTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.1", true)]
    [InlineData("1.2.1", "1.2.1", false)]
    [InlineData("1.3.0", "1.2.9", false)]
    [InlineData("v1.2.1", "1.2.1", false)]
    [InlineData("1.2.1", "v1.2.1", false)]
    [InlineData("1.2.0", "v1.2.1", true)]
    [InlineData("v1.2.0", "v1.2.1", true)]
    [InlineData("1.2.1", "1.2.2-beta", true)]
    public void CheckForUpdates_HandlesSemanticVersionComparisonsCorrectly(string current, string remote, bool expected)
    {
        bool isUpdateAvailable = Updater.IsUpdateAvailable(current, remote);
        isUpdateAvailable.Should().Be(expected);
    }

    [Fact]
    public async Task CheckForUpdates_WhenNewerRemoteAvailable_ReturnsTrue()
    {
        var responseJson = "{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://github.com/lacedawn/osu-tracker-linux/releases/tag/v99.0.0\",\"body\":\"Release notes\"}";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        bool result = await Updater.CheckForUpdates("lacedawn/osu-tracker-linux", httpClient);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForUpdates_WhenRemoteIsOlder_ReturnsFalse()
    {
        var responseJson = "{\"tag_name\":\"v0.0.1\",\"html_url\":\"https://github.com/lacedawn/osu-tracker-linux/releases/tag/v0.0.1\",\"body\":\"Release notes\"}";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        bool result = await Updater.CheckForUpdates("lacedawn/osu-tracker-linux", httpClient);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdates_QueriesConfiguredRepository()
    {
        string queriedPath = "";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                queriedPath = req.RequestUri?.AbsolutePath ?? "";
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"tag_name\":\"v1.0.0\"}")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        await Updater.CheckForUpdates("lacedawn/osu-tracker-linux", httpClient);

        queriedPath.Should().Contain("lacedawn/osu-tracker-linux");
    }

    [Fact]
    public async Task NonSuccess_LogsAndReturnsFalse()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("{}")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        bool result = await Updater.CheckForUpdates("lacedawn/osu-tracker-linux", httpClient);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_RespectsToken()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((_, ct) => ct.ThrowIfCancellationRequested())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"tag_name\":\"v99.0.0\"}")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        using var cts = new CancellationTokenSource();

        cts.Cancel();

        bool result = await Updater.CheckForUpdates("lacedawn/osu-tracker-linux", httpClient, cts.Token);

        result.Should().BeFalse();
    }
}
