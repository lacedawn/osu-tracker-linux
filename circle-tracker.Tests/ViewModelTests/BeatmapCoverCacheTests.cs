using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Circle_Tracker.Converters;
using Circle_Tracker.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CircleTracker.Tests.ViewModelTests;

public class BeatmapCoverCacheTests : IDisposable
{
    private readonly string _testCacheDir;
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public BeatmapCoverCacheTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), $"ct_test_covers_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testCacheDir))
        {
            try
            {
                Directory.Delete(_testCacheDir, true);
            }
            catch
            {
            }
        }
    }

    private static Bitmap CreateTestBitmap()
    {
        using var stream = new MemoryStream(ValidPngBytes);
        return new Bitmap(stream);
    }

    [AvaloniaFact]
    public async Task GetCoverAsync_WhenCachedInMemory_ReturnsImmediatelyWithoutHttp()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var testBitmap = CreateTestBitmap();
        var beatmapSetId = 12345;

        cache.SetMemoryCache(beatmapSetId, testBitmap);

        var result = await cache.GetCoverAsync(beatmapSetId);

        result.Should().BeSameAs(testBitmap);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task GetCoverAsync_WhenInvalidOrZeroId_ReturnsNullWithoutRequest()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);

        var resultZero = await cache.GetCoverAsync(0);
        var resultNegative = await cache.GetCoverAsync(-5);

        resultZero.Should().BeNull();
        resultNegative.Should().BeNull();

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task GetCoverAsync_ConcurrentRequestsForSameId_DeduplicatesToSingleHttpCall()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                await Task.Delay(100);
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(ValidPngBytes)
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var beatmapSetId = 98765;

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => cache.GetCoverAsync(beatmapSetId))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(5);
        foreach (var result in results)
        {
            result.Should().NotBeNull();
            result.Should().BeSameAs(results[0]);
        }

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task GetCoverAsync_Http404_CachesNullAndDoesNotRetry()
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
                StatusCode = HttpStatusCode.NotFound
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var beatmapSetId = 40404;

        var firstResult = await cache.GetCoverAsync(beatmapSetId);
        var secondResult = await cache.GetCoverAsync(beatmapSetId);

        firstResult.Should().BeNull();
        secondResult.Should().BeNull();

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [AvaloniaFact]
    public void Convert_WhenCachedInMemory_ReturnsBitmapSynchronously()
    {
        var testBitmap = CreateTestBitmap();
        var beatmapSetId = 55555;
        BeatmapCoverCache.Instance.SetMemoryCache(beatmapSetId, testBitmap);

        var converter = new BeatmapCoverConverter();
        var result = converter.Convert(beatmapSetId, typeof(Bitmap), null, CultureInfo.InvariantCulture);

        result.Should().BeSameAs(testBitmap);
    }

    [AvaloniaFact]
    public void Convert_WhenInvalidOrUncached_ReturnsNull()
    {
        var converter = new BeatmapCoverConverter();

        var resultZero = converter.Convert(0, typeof(Bitmap), null, CultureInfo.InvariantCulture);
        var resultNegative = converter.Convert(-1, typeof(Bitmap), null, CultureInfo.InvariantCulture);
        var resultString = converter.Convert("invalid", typeof(Bitmap), null, CultureInfo.InvariantCulture);
        var resultNull = converter.Convert(null, typeof(Bitmap), null, CultureInfo.InvariantCulture);

        resultZero.Should().BeNull();
        resultNegative.Should().BeNull();
        resultString.Should().BeNull();
        resultNull.Should().BeNull();
    }

    [AvaloniaFact]
    public void AttachedProperty_WhenCachedInMemory_SetsImageSourceSynchronously()
    {
        var testBitmap = CreateTestBitmap();
        var beatmapSetId = 77777;
        BeatmapCoverCache.Instance.SetMemoryCache(beatmapSetId, testBitmap);

        var image = new Image();
        BeatmapCoverConverter.SetBeatmapSetId(image, beatmapSetId);

        image.Source.Should().BeSameAs(testBitmap);
    }

    private sealed class TrackingBitmap : Bitmap
    {
        public int DisposeCount;

        public TrackingBitmap(MemoryStream stream)
            : base(stream)
        {
        }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }

    private static TrackingBitmap CreateTrackingBitmap()
    {
        return new TrackingBitmap(new MemoryStream(ValidPngBytes));
    }

    [AvaloniaFact]
    public void CoverCache_WhenEvicted_OldBitmapDisposed()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var first = CreateTrackingBitmap();

        cache.SetMemoryCache(1, first);

        for (int id = 2; id <= BeatmapCoverCache.MaxMemoryCacheSize + 1; id++)
        {
            cache.SetMemoryCache(id, CreateTrackingBitmap());
        }

        first.DisposeCount.Should().Be(1);
    }

    [AvaloniaFact]
    public void CoverCache_WhenOverCapacity_EvictsOldestEntry()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);

        cache.SetMemoryCache(1, CreateTestBitmap());

        for (int id = 2; id <= BeatmapCoverCache.MaxMemoryCacheSize + 1; id++)
        {
            cache.SetMemoryCache(id, CreateTestBitmap());
        }

        cache.TryGetFromMemory(1, out _).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task CoverCache_WhenOneWaiterCancels_OtherWaitersStillComplete()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(ValidPngBytes)
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var beatmapSetId = 31337;
        using var cts = new CancellationTokenSource();

        Task<Bitmap?> waiterOne = cache.GetCoverAsync(beatmapSetId, cts.Token);
        Task<Bitmap?> waiterTwo = cache.GetCoverAsync(beatmapSetId, CancellationToken.None);

        await entered.Task;
        cts.Cancel();
        release.TrySetResult(true);

        Bitmap? resultTwo = null;

        try
        {
            resultTwo = await waiterTwo;
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await waiterOne;
        }
        catch (OperationCanceledException)
        {
        }

        resultTwo.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task CoverCache_WhenWaiterCancels_CancelledWaiterThrows()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(ValidPngBytes)
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var cache = new BeatmapCoverCache(httpClient, _testCacheDir);
        var beatmapSetId = 31338;
        using var cts = new CancellationTokenSource();

        Task<Bitmap?> waiterOne = cache.GetCoverAsync(beatmapSetId, cts.Token);
        Task<Bitmap?> waiterTwo = cache.GetCoverAsync(beatmapSetId, CancellationToken.None);

        await entered.Task;
        cts.Cancel();
        release.TrySetResult(true);

        Func<Task> act = async () => await waiterOne;

        await act.Should().ThrowAsync<OperationCanceledException>();

        try
        {
            await waiterTwo;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
