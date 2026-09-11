namespace Circle_Tracker.Sync;

public record MigrationProgress(
    int ProcessedRows,
    int TotalRows,
    int ImportedCount,
    int SkippedDuplicates,
    string CurrentBeatmapString,
    double ProgressPercent
);

public record SyncResult(
    bool Success,
    int SyncedCount,
    int FailedCount,
    string? ErrorMessage,
    int SkippedCount = 0
)
{
    public bool IsSuccess => Success;
}

public enum ExportFormat
{
    Csv,
    Json,
    SqliteBackup
}
