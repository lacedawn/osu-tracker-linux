using Avalonia.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Services;

public class BeatmapCoverCache
{
    private readonly ConcurrentDictionary<int, Bitmap?> _memoryCache = new();
    private readonly ConcurrentDictionary<int, Task<Bitmap?>> _inFlightRequests = new();
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
        return _memoryCache.TryGetValue(beatmapSetId, out var bitmap) ? bitmap : null;
    }

    public bool TryGetFromMemory(int beatmapSetId, out Bitmap? bitmap)
    {
        return _memoryCache.TryGetValue(beatmapSetId, out bitmap);
    }

    public void SetMemoryCache(int beatmapSetId, Bitmap? bitmap)
    {
        _memoryCache[beatmapSetId] = bitmap;
    }

    public void PrepopulateCache(int beatmapSetId, Bitmap? bitmap)
    {
        _memoryCache[beatmapSetId] = bitmap;
    }

    public void Clear()
    {
        _memoryCache.Clear();
        _inFlightRequests.Clear();
    }

    public async Task<Bitmap?> GetCoverAsync(int beatmapSetId, CancellationToken ct = default)
    {
        if (beatmapSetId <= 0)
        {
            return null;
        }

        if (_memoryCache.TryGetValue(beatmapSetId, out var cached))
        {
            return cached;
        }

        Task<Bitmap?> loadTask;
        lock (_syncLock)
        {
            if (_memoryCache.TryGetValue(beatmapSetId, out cached))
            {
                return cached;
            }

            if (!_inFlightRequests.TryGetValue(beatmapSetId, out loadTask!))
            {
                loadTask = LoadCoverInternalAsync(beatmapSetId, ct);
                _inFlightRequests[beatmapSetId] = loadTask;
            }
        }

        try
        {
            return await loadTask;
        }
        finally
        {
            _inFlightRequests.TryRemove(beatmapSetId, out _);
        }
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
                _memoryCache[beatmapSetId] = diskBitmap;
                CoverLoaded?.Invoke(this, (beatmapSetId, diskBitmap));
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
            using var response = await _httpClient.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _memoryCache[beatmapSetId] = null;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            if (diskPath != null)
            {
                try
                {
                    await File.WriteAllBytesAsync(diskPath, bytes, ct);
                }
                catch
                {
                }
            }

            using var memoryStream = new MemoryStream(bytes);
            var downloadedBitmap = new Bitmap(memoryStream);
            _memoryCache[beatmapSetId] = downloadedBitmap;
            CoverLoaded?.Invoke(this, (beatmapSetId, downloadedBitmap));
            return downloadedBitmap;
        }
        catch
        {
            return null;
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
