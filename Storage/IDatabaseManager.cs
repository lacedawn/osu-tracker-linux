using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Circle_Tracker.Storage
{
    public interface IDatabaseManager : IDisposable, IAsyncDisposable
    {
        string DatabasePath { get; }
        bool IsHealthy { get; }
        Task InitializeAsync(CancellationToken ct = default);
        SqliteConnection CreateConnection();
        Task<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default);
        Task ExecuteInTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action, CancellationToken ct = default);
        Task<T> ExecuteInTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action, CancellationToken ct = default);
    }
}
