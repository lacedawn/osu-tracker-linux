using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage.Querying
{
    public interface IPlayQueryEngine
    {
        Task<PagedResult<PlayRecord>> QueryPlaysAsync(PlayQueryFilter filter, CancellationToken ct = default);
        Task<PlayFilterSummary> GetSummaryOnlyAsync(PlayQueryFilter filter, CancellationToken ct = default);
        Task<IReadOnlyList<string>> AutocompleteSearchAsync(string term, int limit = 10, CancellationToken ct = default);
    }
}
