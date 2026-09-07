using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker;

public interface ITosuTransport
{
    Task<bool> RunWebSocketSessionAsync(byte[] buffer, CancellationToken ct);
    Task<TosuState?> PollHttpSnapshotAsync(CancellationToken ct);
}
