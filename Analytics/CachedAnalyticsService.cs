using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public class CachedAnalyticsService : IAnalyticsService
    {
        private readonly ISkillAnalyticsService _skillService;
        private readonly ISessionAnalyticsService _sessionService;
        private readonly TimeSpan _defaultTtl;
        private readonly ConcurrentDictionary<string, (object Data, DateTime ExpiresAt)> _cache = new();

        public CachedAnalyticsService(
            ISkillAnalyticsService skillService,
            ISessionAnalyticsService sessionService,
            TimeSpan? defaultTtl = null)
        {
            _skillService = skillService;
            _sessionService = sessionService;
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
        }

        public void InvalidateCache()
        {
            _cache.Clear();
        }

        private async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
        {
            DateTime now = DateTime.UtcNow;
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > now && entry.Data is T cachedVal)
                {
                    return cachedVal;
                }
            }

            T fresh = await factory();
            _cache[key] = (fresh!, now.Add(_defaultTtl));
            return fresh;
        }

        public Task<IReadOnlyList<StarMasteryBracket>> GetStarMasteryCurveAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("skill:star_mastery", () => _skillService.GetStarMasteryCurveAsync(ct));
        }

        public Task<AimSpeedProfile> GetAimSpeedProfileAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("skill:aim_speed", () => _skillService.GetAimSpeedProfileAsync(ct));
        }

        public Task<IReadOnlyList<OdAccuracyTier>> GetOdAccuracyCurveAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("skill:od_curve", () => _skillService.GetOdAccuracyCurveAsync(ct));
        }

        public Task<IReadOnlyList<BpmBracketStats>> GetBpmSpeedCeilingsAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("skill:bpm_speed", () => _skillService.GetBpmSpeedCeilingsAsync(ct));
        }

        public Task<IReadOnlyList<FatigueBucket>> GetSessionFatigueCurveAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("session:fatigue_curve", () => _sessionService.GetSessionFatigueCurveAsync(ct));
        }

        public Task<Dictionary<string, RollingPeriodStats>> GetRollingAveragesAsync(CancellationToken ct = default)
        {
            return GetOrCreateAsync("session:rolling_averages", () => _sessionService.GetRollingAveragesAsync(ct));
        }

        public Task<IReadOnlyList<ChokeMapRecord>> GetTopChokeMapsAsync(int limit = 10, CancellationToken ct = default)
        {
            return GetOrCreateAsync($"session:chokes:{limit}", () => _sessionService.GetTopChokeMapsAsync(limit, ct));
        }

        public Task<HeadToHeadComparison> CompareSessionToBaselineAsync(string sessionId, CancellationToken ct = default)
        {
            return GetOrCreateAsync($"session:h2h:{sessionId}", () => _sessionService.CompareSessionToBaselineAsync(sessionId, ct));
        }
    }
}
