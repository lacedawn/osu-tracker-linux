using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public interface ISkillAnalyticsService
    {
        Task<IReadOnlyList<StarMasteryBracket>> GetStarMasteryCurveAsync(CancellationToken ct = default);
        Task<IReadOnlyList<OdAccuracyTier>> GetOdAccuracyCurveAsync(CancellationToken ct = default);
        Task<IReadOnlyList<BpmBracketStats>> GetBpmSpeedCeilingsAsync(CancellationToken ct = default);
    }
}
