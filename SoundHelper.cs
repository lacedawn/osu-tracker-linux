using Microsoft.Extensions.Logging;
using Silk.NET.OpenAL;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public static class SoundHelper
    {
        private static readonly ILogger _log = AppLogger.Factory.CreateLogger(nameof(SoundHelper));
        private static readonly Lazy<string?> _cachedLinuxPlayer = new(ProbeLinuxPlayer);
        private static readonly SemaphoreSlim _fallbackSemaphore = new(2, 2);

        static SoundHelper()
        {
            try
            {
                string defaultPath = Path.Combine(AppContext.BaseDirectory, "assets", "sectionpass.wav");
                if (File.Exists(defaultPath))
                {
                    PreloadSound(defaultPath);
                }
            }
            catch
            {
            }
        }

        public static void PreloadSound(string path)
        {
            if (File.Exists(path) && OpenAlAudioEngine.IsAvailable)
            {
                OpenAlAudioEngine.Instance.Preload(path);
            }
        }

        public static void PlaySound(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _log.LogWarning("Sound file not found: {Path}", path);
                return;
            }

            _ = Task.Run(() => PlaySoundAsync(path));
        }

        public static void Shutdown()
        {
            if (OpenAlAudioEngine.IsAvailable && OpenAlAudioEngine._instance.IsValueCreated)
            {
                try
                {
                    OpenAlAudioEngine.Instance.Dispose();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Error disposing OpenAL audio engine during shutdown");
                }
            }
        }

        public static async Task PlaySoundAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _log.LogWarning("Sound file not found: {Path}", path);
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                await PlayWindowsSoundAsync(path);
                return;
            }

            if (OpenAlAudioEngine.IsAvailable)
            {
                bool played = await OpenAlAudioEngine.Instance.PlayAsync(path, ct);
                if (played)
                {
                    _log.LogDebug("Played sound via OpenAL: {Path}", path);
                    return;
                }
                else
                {
                    _log.LogWarning("OpenAL failed to play sound, falling back to system player");
                }
            }
            else
            {
                _log.LogDebug("OpenAL not available, using system audio player");
            }

            if (OperatingSystem.IsMacOS())
            {
                await PlayMacOsSoundAsync(path, ct);
            }
            else if (OperatingSystem.IsLinux())
            {
                await PlayLinuxFallbackAsync(path, ct);
            }
        }

        [SupportedOSPlatform("windows")]
        private static Task PlayWindowsSoundAsync(string path)
        {
            try
            {
                using var player = new System.Media.SoundPlayer(path);
                player.Play();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Windows audio error");
            }
            return Task.CompletedTask;
        }

        [SupportedOSPlatform("macos")]
        private static async Task PlayMacOsSoundAsync(string path, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo("afplay", $"\"{path}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
                    try
                    {
                        await proc.WaitForExitAsync(linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "macOS audio error");
            }
        }

        private static string? ProbeLinuxPlayer()
        {
            string[] players = ["pw-play", "paplay", "aplay"];
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var player in players)
                {
                    foreach (var dir in dirs)
                    {
                        var candidate = Path.Combine(dir, player);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            foreach (var player in players)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = player,
                        Arguments = "--version",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        return player;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static async Task PlayLinuxFallbackAsync(string path, CancellationToken ct)
        {
            string? player = _cachedLinuxPlayer.Value;
            if (player == null)
            {
                _log.LogWarning("No supported audio player found (pw-play, paplay, aplay)");
                return;
            }

            string playerName = Path.GetFileName(player);
            _log.LogDebug("Using Linux audio player: {Player}", playerName);

            await _fallbackSemaphore.WaitAsync(ct);
            try
            {
                // Add volume control based on player type
                string args;
                if (playerName == "pw-play")
                {
                    // PipeWire: use --volume (0.0 to 1.0, max valid value)
                    args = $"--volume=1.0 \"{path}\"";
                }
                else if (playerName == "paplay")
                {
                    // PulseAudio: use --volume (0-65536, 65536 = 100%)
                    args = $"--volume=327680 \"{path}\""; // 500% volume
                }
                else if (playerName == "aplay")
                {
                    // ALSA: no built-in volume control, use amixer or just play normally
                    args = $"\"{path}\"";
                }
                else
                {
                    args = $"\"{path}\"";
                }

                var psi = new ProcessStartInfo(player, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return;
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await proc.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _log.LogWarning("Audio player '{Player}' timed out; killing", player);
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to play audio with fallback player {Player}", player);
            }
            finally
            {
                _fallbackSemaphore.Release();
            }
        }

        private sealed class OpenAlAudioEngine : IDisposable
        {
            private const int MaxConcurrentSources = 16;
            internal static readonly Lazy<OpenAlAudioEngine?> _instance = new(CreateEngine);

            private readonly ALContext _alc;
            private readonly AL _al;
            private IntPtr _device;
            private IntPtr _context;
            private readonly SemaphoreSlim _semaphore = new(MaxConcurrentSources, MaxConcurrentSources);
            private readonly ConcurrentQueue<uint> _sourcePool = new();
            private readonly ConcurrentDictionary<string, (uint BufferId, int DurationMs)> _bufferCache = new();
            private readonly object _lock = new();

            public static bool IsAvailable => _instance.Value != null;
            public static OpenAlAudioEngine Instance => _instance.Value ?? throw new InvalidOperationException("OpenAL is not available");

            private static unsafe OpenAlAudioEngine? CreateEngine()
            {
                try
                {
                    var alc = ALContext.GetApi();
                    var al = AL.GetApi();
                    var device = alc.OpenDevice(string.Empty);
                    if (device == null)
                    {
                        return null;
                    }

                    var context = alc.CreateContext(device, (int*)null);
                    if (context == null)
                    {
                        alc.CloseDevice(device);
                        return null;
                    }

                    alc.MakeContextCurrent(context);
                    return new OpenAlAudioEngine(alc, al, (IntPtr)device, (IntPtr)context);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "OpenAL audio engine initialization failed; falling back to alternative players");
                    return null;
                }
            }

            private OpenAlAudioEngine(ALContext alc, AL al, IntPtr device, IntPtr context)
            {
                _alc = alc;
                _al = al;
                _device = device;
                _context = context;

                for (int i = 0; i < MaxConcurrentSources; i++)
                {
                    uint source = _al.GenSource();
                    if (_al.GetError() == AudioError.NoError && source != 0)
                    {
                        _sourcePool.Enqueue(source);
                    }
                }
            }

            public void Preload(string path)
            {
                GetOrLoadBuffer(path);
            }

            public async Task<bool> PlayAsync(string path, CancellationToken ct = default)
            {
                var bufferInfo = GetOrLoadBuffer(path);
                if (bufferInfo == null)
                {
                    return false;
                }

                var (bufferId, durationMs) = bufferInfo.Value;

                await _semaphore.WaitAsync(ct);
                uint source = 0;
                try
                {
                    if (!_sourcePool.TryDequeue(out source))
                    {
                        lock (_lock)
                        {
                            source = _al.GenSource();
                        }
                    }

                    if (source == 0)
                    {
                        return false;
                    }

                    lock (_lock)
                    {
                        // Drain any stale error state before starting
                        _al.GetError();

                        // Ensure source is stopped and unbound before reuse
                        _al.SourceStop(source);
                        // Drain error from SourceStop (harmless if source wasn't playing)
                        _al.GetError();

                        _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                        // Drain error from unbind (harmless if no buffer was bound)
                        _al.GetError();
                        
                        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)bufferId);
                        var error = _al.GetError();
                        if (error != AudioError.NoError)
                        {
                            _log.LogWarning("OpenAL SetSourceProperty failed with error {Error} for path {Path}", error, path);
                            return false;
                        }

                        // Set volume to 500% (5.0 = much louder notification sound)
                        _al.SetSourceProperty(source, SourceFloat.Gain, 5.0f);
                        
                        _al.SourcePlay(source);
                        error = _al.GetError();
                        if (error != AudioError.NoError)
                        {
                            _log.LogWarning("OpenAL SourcePlay failed with error {Error} for path {Path}", error, path);
                            return false;
                        }
                    }

                    int waitMs = Math.Clamp(durationMs, 50, 10000);
                    try
                    {
                        await Task.Delay(waitMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        lock (_lock)
                        {
                            _al.SourceStop(source);
                        }
                        throw;
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to play sound via OpenAL for path {Path}", path);
                    return false;
                }
                finally
                {
                    if (source != 0)
                    {
                        lock (_lock)
                        {
                            // Must stop the source before unbinding the buffer,
                            // otherwise OpenAL returns AL_INVALID_OPERATION (IllegalCommand)
                            _al.SourceStop(source);
                            _al.GetError(); // drain stop error
                            _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                            _al.GetError(); // drain unbind error
                        }
                        _sourcePool.Enqueue(source);
                    }
                    _semaphore.Release();
                }
            }

            private (uint BufferId, int DurationMs)? GetOrLoadBuffer(string path)
            {
                string fullPath = Path.GetFullPath(path);
                if (_bufferCache.TryGetValue(fullPath, out var cached))
                {
                    return cached;
                }

                lock (_lock)
                {
                    if (_bufferCache.TryGetValue(fullPath, out cached))
                    {
                        return cached;
                    }

                    var loaded = LoadWavToBuffer(fullPath);
                    if (loaded != null)
                    {
                        _bufferCache[fullPath] = loaded.Value;
                        return loaded.Value;
                    }
                }

                return null;
            }

            private (uint BufferId, int DurationMs)? LoadWavToBuffer(string path)
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    using var reader = new BinaryReader(stream);

                    if (stream.Length < 12)
                    {
                        return null;
                    }

                    var riff = new string(reader.ReadChars(4));
                    if (riff != "RIFF")
                    {
                        return null;
                    }

                    int fileSize = reader.ReadInt32();
                    var wave = new string(reader.ReadChars(4));
                    if (wave != "WAVE")
                    {
                        return null;
                    }

                    short channels = 0;
                    int sampleRate = 0;
                    short bitsPerSample = 0;
                    byte[]? pcmData = null;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        var chunkId = new string(reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();

                        if (chunkId == "fmt ")
                        {
                            short formatTag = reader.ReadInt16();
                            channels = reader.ReadInt16();
                            sampleRate = reader.ReadInt32();
                            int byteRate = reader.ReadInt32();
                            short blockAlign = reader.ReadInt16();
                            bitsPerSample = reader.ReadInt16();
                            int extra = chunkSize - 16;
                            if (extra > 0 && stream.Position + extra <= stream.Length)
                            {
                                reader.ReadBytes(extra);
                            }
                        }
                        else if (chunkId == "data")
                        {
                            if (chunkSize > 0 && stream.Position + chunkSize <= stream.Length)
                            {
                                pcmData = reader.ReadBytes(chunkSize);
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (chunkSize > 0 && stream.Position + chunkSize <= stream.Length)
                            {
                                reader.ReadBytes(chunkSize);
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (chunkSize % 2 != 0 && stream.Position < stream.Length)
                        {
                            reader.ReadByte();
                        }
                    }

                    if (pcmData == null || channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                    {
                        return null;
                    }

                    BufferFormat format = (channels, bitsPerSample) switch
                    {
                        (1, 8) => BufferFormat.Mono8,
                        (1, 16) => BufferFormat.Mono16,
                        (2, 8) => BufferFormat.Stereo8,
                        (2, 16) => BufferFormat.Stereo16,
                        _ => (BufferFormat)0
                    };

                    if (format == 0)
                    {
                        return null;
                    }

                    uint bufferId = _al.GenBuffer();
                    if (_al.GetError() != AudioError.NoError || bufferId == 0)
                    {
                        return null;
                    }

                    _al.BufferData(bufferId, format, pcmData, sampleRate);
                    if (_al.GetError() != AudioError.NoError)
                    {
                        _al.DeleteBuffer(bufferId);
                        return null;
                    }

                    int bytesPerSec = sampleRate * channels * (bitsPerSample / 8);
                    int durationMs = bytesPerSec > 0 ? (int)((long)pcmData.Length * 1000 / bytesPerSec) : 0;
                    return (bufferId, durationMs);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load WAV buffer from {Path}", path);
                    return null;
                }
            }

            public unsafe void Dispose()
            {
                lock (_lock)
                {
                    while (_sourcePool.TryDequeue(out uint source))
                    {
                        _al.DeleteSource(source);
                    }

                    foreach (var entry in _bufferCache.Values)
                    {
                        _al.DeleteBuffer(entry.BufferId);
                    }
                    _bufferCache.Clear();

                    if (_context != IntPtr.Zero)
                    {
                        _alc.MakeContextCurrent((Context*)null);
                        _alc.DestroyContext((Context*)_context);
                        _context = IntPtr.Zero;
                    }

                    if (_device != IntPtr.Zero)
                    {
                        _alc.CloseDevice((Device*)_device);
                        _device = IntPtr.Zero;
                    }
                }
                _semaphore.Dispose();
            }
        }
    }
}
