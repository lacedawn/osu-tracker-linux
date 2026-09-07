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

        public string SinkName => "Composite";
        public IReadOnlyList<SinkRegistration> Registrations => _registrations.AsReadOnly();

        public bool IsReady => _registrations
            .Where(r => r.IsEnabled())
            .Any(r => r.Sink.IsReady);

        public bool AllSinksReady => _registrations
            .Where(r => r.IsEnabled())
            .All(r => r.Sink.IsReady);

        public CompositePlaySink(IEnumerable<SinkRegistration>? registrations = null)
        {
            if (registrations != null)
            {
                _registrations.AddRange(registrations);
            }
        }

        public void AddSink(IPlaySink sink, Func<bool>? isEnabled = null)
        {
            _registrations.Add(new SinkRegistration(sink, isEnabled));
        }

        public T? GetSink<T>() where T : class, IPlaySink
        {
            return _registrations.Select(r => r.Sink).OfType<T>().FirstOrDefault();
        }

        public async Task InitializeAsync(bool silent = false, CancellationToken ct = default)
        {
            var tasks = _registrations.Select(async r =>
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

        public async Task TryLogPlayAsync(PlayEntryData data, PlayContext context, CancellationToken ct = default)
        {
            var activeSinks = _registrations.Where(r => r.IsEnabled()).Select(r => r.Sink).ToList();
            if (activeSinks.Count == 0) return;

            var sheetsSink = activeSinks.FirstOrDefault(s => s.SinkName == "Google Sheets" || s.SinkName == "Google Sheets Adapter");
            var sqliteSink = activeSinks.FirstOrDefault(s => s.SinkName == "Local SQLite");

            bool? sheetsSyncSucceeded = false;

            if (sheetsSink != null && sheetsSink.IsReady)
            {
                try
                {
                    await sheetsSink.TryLogPlayAsync(data, context, ct);
                    sheetsSyncSucceeded = true;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error logging play to sink {SinkName}", sheetsSink.SinkName);
                    sheetsSyncSucceeded = false;
                }
            }
            else if (sheetsSink != null && !sheetsSink.IsReady)
            {
                sheetsSyncSucceeded = false;
            }

            var updatedContext = context with { SheetsSyncSucceeded = sheetsSyncSucceeded };

            var remainingSinks = activeSinks.Where(s => s != sheetsSink).ToList();
            var tasks = remainingSinks.Select(async sink =>
            {
                try
                {
                    await sink.TryLogPlayAsync(data, updatedContext, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error logging play to sink {SinkName}", sink.SinkName);
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
