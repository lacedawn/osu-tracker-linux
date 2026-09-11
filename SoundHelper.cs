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

        internal interface IWindowsSoundPlayer : IDisposable
        {
            void PlaySync();
        }

        [SupportedOSPlatform("windows")]
        private sealed class SoundPlayerAdapter : IWindowsSoundPlayer
        {
            private readonly System.Media.SoundPlayer _player;

            public SoundPlayerAdapter(string path)
            {
                _player = new System.Media.SoundPlayer(path);
            }

            public void PlaySync() => _player.PlaySync();

            public void Dispose() => _player.Dispose();
        }

        internal static Func<string, IWindowsSoundPlayer>? WindowsSoundPlayerFactory;

        internal static string EscapePlayerPath(string path)
        {
            return path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        internal static string BuildPlayerArguments(string playerName, string path)
        {
            string escaped = EscapePlayerPath(path);

            if (playerName == "pw-play")
            {
                return $"--volume=1.0 \"{escaped}\"";
            }

            if (playerName == "paplay")
            {
                return $"--volume=65536 \"{escaped}\"";
            }

            return $"\"{escaped}\"";
        }

        [SupportedOSPlatform("windows")]
        internal static async Task PlayWindowsSoundAsync(string path)
        {
            try
            {
                Func<string, IWindowsSoundPlayer> factory = WindowsSoundPlayerFactory ?? (static p => new SoundPlayerAdapter(p));

                await Task.Run(() =>
                {
                    using IWindowsSoundPlayer player = factory(path);
                    player.PlaySync();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Windows audio error");
            }
        }

        [SupportedOSPlatform("macos")]
        private static async Task PlayMacOsSoundAsync(string path, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo("afplay", $"\"{EscapePlayerPath(path)}\"")
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

        internal static readonly TimeSpan LinuxProbeTimeout = TimeSpan.FromSeconds(2);

        private static string? ProbeLinuxPlayer()
        {
            string[] players = ["pw-play", "paplay", "aplay"];
            return ProbeLinuxPlayer(players, static psi => Process.Start(psi), LinuxProbeTimeout);
        }

        internal static string? ProbeLinuxPlayer(string[] players, Func<ProcessStartInfo, Process?> starter, TimeSpan timeout)
        {
            foreach (var player in players)
            {
                Process? proc = null;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = player,
                        Arguments = "--version",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    proc = starter(psi);

                    if (proc == null)
                    {
                        continue;
                    }

                    bool exited = proc.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));

                    if (exited)
                    {
                        return player;
                    }

                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        proc.WaitForExit(500);
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }
                finally
                {
                    try
                    {
                        proc?.Dispose();
                    }
                    catch
                    {
                    }
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
                string args = BuildPlayerArguments(playerName, path);

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

                Task<string> stderrDrain = proc.StandardError.ReadToEndAsync();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
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

                try
                {
                    string stderr = await stderrDrain.ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        _log.LogDebug("Audio player '{Player}' stderr: {Stderr}", player, stderr.Trim());
                    }
                }
                catch
                {
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
                        _al.GetError();

                        _al.SourceStop(source);
                        _al.GetError();

                        _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                        _al.GetError();
                        
                        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)bufferId);
                        var error = _al.GetError();
                        if (error != AudioError.NoError)
                        {
                            _log.LogWarning("OpenAL SetSourceProperty failed with error {Error} for path {Path}", error, path);
                            return false;
                        }

                        _al.SetSourceProperty(source, SourceFloat.Gain, 0.8f);
                        
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
                            _al.SourceStop(source);
                            _al.GetError();
                            _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                            _al.GetError();
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
