using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Analytics
{
    public interface IAnalyticsService : ISkillAnalyticsService, ISessionAnalyticsService
    {
        void InvalidateCache();
    }
}
