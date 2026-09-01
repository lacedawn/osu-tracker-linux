using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker
{
    public interface ITosuClient
    {
        bool IsConnected { get; }
        TosuState? LatestState { get; }
        string Host { get; set; }
        int Port { get; set; }
        event EventHandler<bool>? ConnectionStateChanged;
        event EventHandler<TosuState>? StateUpdated;
        Task ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync();
        Task ReconnectAsync();
        Task<PpCalcResult?> CalculatePpAsync(int modNumber = 0, CancellationToken ct = default);
    }
}
