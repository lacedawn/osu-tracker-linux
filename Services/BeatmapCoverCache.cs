using Avalonia.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public class BeatmapCoverCache
{
    internal const int MaxMemoryCacheSize = 50;

    private readonly ConcurrentDictionary<int, Bitmap?> _memoryCache = new();
    private readonly ConcurrentDictionary<int, Task<Bitmap?>> _inFlightRequests = new();
    private readonly LinkedList<int> _lruOrder = new();
    private readonly HttpClient _httpClient;
    private readonly string? _cacheDirectory;
    private readonly object _syncLock = new();

    public static BeatmapCoverCache Instance { get; set; } = new();

    public event EventHandler<(int BeatmapSetId, Bitmap? Bitmap)>? CoverLoaded;

    public BeatmapCoverCache(HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("circle-tracker/1.0 (osu! session tracker)");
        }

        if (cacheDirectory != null)
        {
            _cacheDirectory = cacheDirectory;
        }
        else
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "circle-tracker",
                "cache",
                "covers"
            );
        }

        if (!string.IsNullOrEmpty(_cacheDirectory))
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch
            {
            }
        }
    }

    public BeatmapCoverCache(HttpMessageHandler handler, string? cacheDirectory = null)
        : this(new HttpClient(handler), cacheDirectory)
    {
    }

    public Bitmap? GetFromMemory(int beatmapSetId)
    {
        lock (_syncLock)
        {
            if (_memoryCache.TryGetValue(beatmapSetId, out var bitmap))
            {
                TouchLocked(beatmapSetId);
                return bitmap;
            }

            return null;
        }
    }

    public bool TryGetFromMemory(int beatmapSetId, out Bitmap? bitmap)
    {
        lock (_syncLock)
        {
            if (_memoryCache.TryGetValue(beatmapSetId, out bitmap))
            {
                TouchLocked(beatmapSetId);
                return true;
            }

            bitmap = null;
            return false;
        }
    }

    public void SetMemoryCache(int beatmapSetId, Bitmap? bitmap)
    {
        Bitmap? old;
        List<Bitmap?> evicted;

        lock (_syncLock)
        {
            _memoryCache.TryGetValue(beatmapSetId, out old);
            _memoryCache[beatmapSetId] = bitmap;
            TouchLocked(beatmapSetId);
            evicted = EvictIfNeededLocked();
        }

        if (!ReferenceEquals(old, bitmap))
        {
            try
            {
                old?.Dispose();
            }
            catch
            {
            }
        }

        foreach (var item in evicted)
        {
            try
            {
                item?.Dispose();
            }
            catch
            {
            }
        }
    }

    public void PrepopulateCache(int beatmapSetId, Bitmap? bitmap)
    {
        SetMemoryCache(beatmapSetId, bitmap);
    }

    public void Clear()
    {
        List<Bitmap?> owned;

        lock (_syncLock)
        {
            owned = new List<Bitmap?>(_memoryCache.Values);
            _memoryCache.Clear();
            _lruOrder.Clear();
            _inFlightRequests.Clear();
        }

        foreach (var bitmap in owned)
        {
            try
            {
                bitmap?.Dispose();
            }
            catch
            {
            }
        }
    }

    public async Task<Bitmap?> GetCoverAsync(int beatmapSetId, CancellationToken ct = default)
    {
        if (beatmapSetId <= 0)
        {
            return null;
        }

        Task<Bitmap?> loadTask;

        lock (_syncLock)
        {
            if (_memoryCache.TryGetValue(beatmapSetId, out var cached))
            {
                TouchLocked(beatmapSetId);
                return cached;
            }

            if (!_inFlightRequests.TryGetValue(beatmapSetId, out loadTask!))
            {
                loadTask = LoadCoverInternalAsync(beatmapSetId, CancellationToken.None);
                _inFlightRequests[beatmapSetId] = loadTask;
            }
        }

        try
        {
            return await loadTask.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (loadTask.IsCompleted)
            {
                _inFlightRequests.TryRemove(new KeyValuePair<int, Task<Bitmap?>>(beatmapSetId, loadTask));
            }
        }
    }

    private void TouchLocked(int beatmapSetId)
    {
        var node = _lruOrder.Find(beatmapSetId);

        if (node != null)
        {
            _lruOrder.Remove(node);
        }

        _lruOrder.AddLast(beatmapSetId);
    }

    private List<Bitmap?> EvictIfNeededLocked()
    {
        var evictedBitmaps = new List<Bitmap?>();

        while (_memoryCache.Count > MaxMemoryCacheSize && _lruOrder.First != null)
        {
            int oldest = _lruOrder.First.Value;
            _lruOrder.RemoveFirst();

            if (_memoryCache.TryRemove(oldest, out var evicted))
            {
                evictedBitmaps.Add(evicted);
            }
        }

        return evictedBitmaps;
    }

    private (Bitmap? Overwritten, List<Bitmap?> Evicted) StoreMemoryCacheLocked(int beatmapSetId, Bitmap? bitmap)
    {
        _memoryCache.TryGetValue(beatmapSetId, out var old);
        _memoryCache[beatmapSetId] = bitmap;
        TouchLocked(beatmapSetId);
        List<Bitmap?> evicted = EvictIfNeededLocked();
        Bitmap? overwritten = ReferenceEquals(old, bitmap) ? null : old;
        return (overwritten, evicted);
    }

    private static void DisposeBitmaps(Bitmap? overwritten, List<Bitmap?> evicted)
    {
        if (overwritten != null)
        {
            try
            {
                overwritten.Dispose();
            }
            catch
            {
            }
        }

        foreach (var item in evicted)
        {
            try
            {
                item?.Dispose();
            }
            catch
            {
            }
        }
    }

    private void RaiseCoverLoaded(int beatmapSetId, Bitmap? bitmap)
    {
        EventHandler<(int BeatmapSetId, Bitmap? Bitmap)>? handler = CoverLoaded;

        if (handler == null)
        {
            return;
        }

        UiDispatcher.Post(() => handler(this, (beatmapSetId, bitmap)));
    }

    private async Task<Bitmap?> LoadCoverInternalAsync(int beatmapSetId, CancellationToken ct)
    {
        var diskPath = GetDiskCachePath(beatmapSetId);
        if (diskPath != null && File.Exists(diskPath))
        {
            try
            {
                using var stream = File.OpenRead(diskPath);
                var diskBitmap = new Bitmap(stream);
                (Bitmap? Overwritten, List<Bitmap?> Evicted) storeDisk;

                lock (_syncLock)
                {
                    storeDisk = StoreMemoryCacheLocked(beatmapSetId, diskBitmap);
                }

                DisposeBitmaps(storeDisk.Overwritten, storeDisk.Evicted);

                RaiseCoverLoaded(beatmapSetId, diskBitmap);
                return diskBitmap;
            }
            catch
            {
                try
                {
                    File.Delete(diskPath);
                }
                catch
                {
                }
            }
        }

        var url = $"https://assets.ppy.sh/beatmaps/{beatmapSetId}/covers/cover.jpg";
        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                (Bitmap? Overwritten, List<Bitmap?> Evicted) storeNull;

                lock (_syncLock)
                {
                    storeNull = StoreMemoryCacheLocked(beatmapSetId, null);
                }

                DisposeBitmaps(storeNull.Overwritten, storeNull.Evicted);

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            if (diskPath != null)
            {
                try
                {
                    await File.WriteAllBytesAsync(diskPath, bytes, ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            using var memoryStream = new MemoryStream(bytes);
            var downloadedBitmap = new Bitmap(memoryStream);
            (Bitmap? Overwritten, List<Bitmap?> Evicted) storeDownload;

            lock (_syncLock)
            {
                storeDownload = StoreMemoryCacheLocked(beatmapSetId, downloadedBitmap);
            }

            DisposeBitmaps(storeDownload.Overwritten, storeDownload.Evicted);

            RaiseCoverLoaded(beatmapSetId, downloadedBitmap);
            return downloadedBitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlightRequests.TryRemove(beatmapSetId, out _);
        }
    }

    private string? GetDiskCachePath(int beatmapSetId)
    {
        if (string.IsNullOrEmpty(_cacheDirectory))
        {
            return null;
        }
        return Path.Combine(_cacheDirectory, $"{beatmapSetId}.jpg");
    }
}
