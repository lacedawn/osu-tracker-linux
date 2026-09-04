using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public interface ISessionAnalyticsService
    {
        Task<Dictionary<string, RollingPeriodStats>> GetRollingAveragesAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ChokeMapRecord>> GetTopChokeMapsAsync(int limit = 10, CancellationToken ct = default);
        Task<HeadToHeadComparison> CompareSessionToBaselineAsync(string sessionId, CancellationToken ct = default);
        Task<IReadOnlyList<DailyTrendItem>> GetDailyActivityLogAsync(int days = 14, CancellationToken ct = default);
    }
}
