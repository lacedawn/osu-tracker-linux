using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public class SinkRegistration
    {
        public IPlaySink Sink { get; }
        public Func<bool> IsEnabled { get; }

        public SinkRegistration(IPlaySink sink, Func<bool>? isEnabled = null)
        {
            Sink = sink;
            IsEnabled = isEnabled ?? (() => true);
        }
    }

    public class CompositePlaySink : IPlaySink
    {
        private static readonly ILogger<CompositePlaySink> _log = AppLogger.For<CompositePlaySink>();
        private readonly List<SinkRegistration> _registrations = new();
        private readonly object _registrationsLock = new();

        internal TimeSpan SheetsAttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public string SinkName => "Composite";
        public IReadOnlyList<SinkRegistration> Registrations => SnapshotRegistrations();

        public bool IsReady => SnapshotRegistrations()
            .Where(r => r.IsEnabled())
            .Any(r => r.Sink.IsReady);

        public bool AllSinksReady => SnapshotRegistrations()
            .Where(r => r.IsEnabled())
            .All(r => r.Sink.IsReady);

        public CompositePlaySink(IEnumerable<SinkRegistration>? registrations = null)
        {
            if (registrations != null)
            {
                lock (_registrationsLock)
                {
                    _registrations.AddRange(registrations);
                }
            }
        }

        public void AddSink(IPlaySink sink, Func<bool>? isEnabled = null)
        {
            lock (_registrationsLock)
            {
                _registrations.Add(new SinkRegistration(sink, isEnabled));
            }
        }

        public T? GetSink<T>() where T : class, IPlaySink
        {
            return SnapshotRegistrations().Select(r => r.Sink).OfType<T>().FirstOrDefault();
        }

        private SinkRegistration[] SnapshotRegistrations()
        {
            lock (_registrationsLock)
            {
                return _registrations.ToArray();
            }
        }

        public async Task InitializeAsync(bool silent = false, CancellationToken ct = default)
        {
            var snapshot = SnapshotRegistrations();
            var tasks = snapshot.Select(async r =>
            {
                try
                {
                    await r.Sink.InitializeAsync(silent, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to initialize sink {SinkName}", r.Sink.SinkName);
                    if (!silent) throw;
                }
            });
            await Task.WhenAll(tasks);
        }

        private static bool IsSheetsSink(IPlaySink sink)
        {
            return sink.SinkName == "Google Sheets" || sink.SinkName == "Google Sheets Adapter";
        }

        private static async Task<bool> TrySheetsSinkAsync(IPlaySink sheetsSink, PlayEntryData data, PlayContext context, CancellationToken ct)
        {
            try
            {
                if (sheetsSink is ISheetsSink sheetsSyncSink)
                {
                    return await sheetsSyncSink.TryLogPlayAsync(data, context, ct);
                }

                await sheetsSink.TryLogPlayAsync(data, context, ct);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error logging play to sink {SinkName}", sheetsSink.SinkName);
                return false;
            }
        }

        private async Task<bool> AttemptSheetsSyncAsync(IReadOnlyList<IPlaySink> readySheetsSinks, PlayEntryData data, PlayContext context, CancellationToken ct)
        {
            var sheetsTasks = readySheetsSinks.Select(s => TrySheetsSinkAsync(s, data, context, ct)).ToList();
            var allSheets = Task.WhenAll(sheetsTasks);
            var completed = await Task.WhenAny(allSheets, Task.Delay(SheetsAttemptTimeout, ct));
            if (!ReferenceEquals(completed, allSheets))
            {
                _log.LogWarning("Sheets sync timed out after {Timeout}, continuing with local write", SheetsAttemptTimeout);
                _ = allSheets.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _log.LogError(t.Exception, "Late sheets sync failure");
                    }
                }, TaskScheduler.Default);
                return false;
            }

            try
            {
                return (await allSheets).All(succeeded => succeeded);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error logging play to sheets sinks");
                return false;
            }
        }

        private static async Task<bool> WriteToSinkAsync(IPlaySink sink, PlayEntryData data, PlayContext context, CancellationToken ct)
        {
            try
            {
                await sink.TryLogPlayAsync(data, context, ct);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error logging play to sink {SinkName}", sink.SinkName);
                return false;
            }
        }

        private static async Task WriteToSinkThrowOnFailureAsync(IPlaySink sink, PlayEntryData data, PlayContext context, CancellationToken ct)
        {
            try
            {
                await sink.TryLogPlayAsync(data, context, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error logging play to sink {SinkName}", sink.SinkName);
                throw;
            }
        }

        public async Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        {
            var activeSinks = SnapshotRegistrations().Where(r => r.IsEnabled()).Select(r => r.Sink).ToList();
            if (activeSinks.Count == 0) return;

            var sheetsSinks = activeSinks.Where(IsSheetsSink).ToList();
            var sqliteSink = activeSinks.FirstOrDefault(s => s.SinkName == "Local SQLite");
            var otherSinks = activeSinks.Where(s => s != sqliteSink && !IsSheetsSink(s)).ToList();

            var readySheetsSinks = sheetsSinks.Where(s => s.IsReady).ToList();
            bool sheetsAttempted = readySheetsSinks.Count > 0;
            bool sheetsSyncSucceeded;
            if (sheetsSinks.Count == 0)
            {
                sheetsSyncSucceeded = true;
            }
            else if (!sheetsAttempted)
            {
                sheetsSyncSucceeded = false;
            }
            else
            {
                sheetsSyncSucceeded = await AttemptSheetsSyncAsync(readySheetsSinks, data, context, ct);
            }

            var updatedContext = context with { SheetsSyncSucceeded = sheetsSyncSucceeded };

            if (sqliteSink != null)
            {
                await WriteToSinkThrowOnFailureAsync(sqliteSink, data, updatedContext, ct);
            }
            var writeTasks = new List<Task>();
            foreach (var sink in otherSinks)
            {
                writeTasks.Add(WriteToSinkAsync(sink, data, updatedContext, ct));
            }

            await Task.WhenAll(writeTasks);
        }
    }
}
