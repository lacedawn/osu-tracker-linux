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
    public static IEnumerable<object[]> RepositoryResolutionCases => new List<object[]>
    {
        new object[] { default(string)!, Updater.DefaultRepository },
        new object[] { "", Updater.DefaultRepository },
        new object[] { "   ", Updater.DefaultRepository },
        new object[] { "custom-owner/custom-repo", "custom-owner/custom-repo" },
        new object[] { "  custom-owner/custom-repo  ", "custom-owner/custom-repo" },
        new object[] { "no-slash-here", Updater.DefaultRepository },
        new object[] { "too/many/slashes", Updater.DefaultRepository },
        new object[] { "/leading-slash", Updater.DefaultRepository },
        new object[] { "trailing-slash/", Updater.DefaultRepository },
        new object[] { "has space/repo", Updater.DefaultRepository },
        new object[] { "owner/re po", Updater.DefaultRepository },
        new object[] { "owner/repo!", Updater.DefaultRepository },
    };
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

    [Theory]
    [MemberData(nameof(RepositoryResolutionCases))]
    public void ResolveRepository_VariousInputs_ReturnsExpected(string? repository, string expected)
    {
        string result = Updater.ResolveRepository(repository);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task CheckForUpdates_WhenCustomRepositoryConfigured_UsesIt()
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
                Content = new StringContent("{\"tag_name\":\"v99.0.0\"}")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        await Updater.CheckForUpdates("custom-owner/custom-repo", httpClient);

        queriedPath.Should().Contain("custom-owner/custom-repo");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a repo")]
    public async Task CheckForUpdates_WhenRepositoryMissing_UsesDefaultRepository(string? repository)
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

        await Updater.CheckForUpdates(repository, httpClient);

        queriedPath.Should().Contain(Updater.DefaultRepository);
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
