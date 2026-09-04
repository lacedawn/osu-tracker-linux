using System;
using System.Collections.Generic;

namespace Circle_Tracker.Storage.Querying
{
    public enum DateRangePreset
    {
        AllTime,
        Today,
        Yesterday,
        Last7Days,
        Last30Days,
        ThisMonth,
        Custom
    }

    public enum ModFilterMode
    {
        Any,
        NoModOnly,
        ExactMatch,
        ContainsAll,
        ContainsAny
    }

    public enum PassStatusFilter
    {
        All,
        PassesOnly,
        FailsAndRetriesOnly
    }

    public enum ReplayFilterOption
    {
        ExcludeReplays,
        IncludeReplays,
        ReplaysOnly
    }

    public enum PlaySortField
    {
        Timestamp,
        Stars,
        Accuracy,
        Bpm,
        TotalHits,
        PlayTimeSeconds,
        ConsecutivePlayCount
    }

    public enum SortOrder
    {
        Descending,
        Ascending
    }

    public record PlayQueryFilter
    {
        public DateRangePreset DatePreset { get; init; } = DateRangePreset.AllTime;
        public DateTime? StartTimeUtc { get; init; }
        public DateTime? EndTimeUtc { get; init; }

        public ModFilterMode ModMode { get; init; } = ModFilterMode.Any;
        public int? RequiredModsBitfield { get; init; }
        public int? ExcludedModsBitfield { get; init; }

        public double? MinStars { get; init; }
        public double? MaxStars { get; init; }
        public int? MinBpm { get; init; }
        public int? MaxBpm { get; init; }
        public double? MinOd { get; init; }
        public double? MaxOd { get; init; }
        public double? MinCs { get; init; }
        public double? MaxCs { get; init; }
        public double? MinAr { get; init; }
        public double? MaxAr { get; init; }

        public decimal? MinAccuracy { get; init; }
        public decimal? MaxAccuracy { get; init; }
        public int? MinHits { get; init; }
        public int? MaxHits { get; init; }

        public PassStatusFilter PassStatus { get; init; } = PassStatusFilter.All;
        public ReplayFilterOption ReplayFilter { get; init; } = ReplayFilterOption.ExcludeReplays;
        public string? SessionId { get; init; }
        public string? DetectedClient { get; init; }

        public string? SearchQuery { get; init; }
        public string? SearchText => SearchQuery;

        public PlaySortField SortBy { get; init; } = PlaySortField.Timestamp;
        public SortOrder Order { get; init; } = SortOrder.Descending;
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 50;
    }

    public record PlayRecord(
        long Id,
        string? SessionId,
        DateTime TimestampUtc,
        int BeatmapId,
        int BeatmapSetId,
        string? BeatmapChecksum,
        string BeatmapString,
        string BeatmapTitle,
        string BeatmapArtist,
        string BeatmapVersion,
        int ModsBitfield,
        string ModsString,
        int Bpm,
        decimal Stars,
        decimal Aim,
        decimal Speed,
        decimal Cs,
        decimal Ar,
        decimal Od,
        decimal Hp,
        int TotalHits,
        int Hit300,
        int Hit100,
        int Hit50,
        int HitMiss,
        decimal Accuracy,
        bool AccuracyReliable,
        bool IsComplete,
        int PlayTimeSeconds,
        int ConsecutivePlayCount,
        int GameMode,
        bool IsReplay,
        string DetectedClient
    )
    {
        public string CoverUrl => BeatmapSetId > 0 ? $"https://assets.ppy.sh/beatmaps/{BeatmapSetId}/covers/cover.jpg" : "";
    }

    public record PlayFilterSummary(
        int TotalMatches,
        decimal AverageAccuracy,
        decimal AverageStars,
        int TotalPlayTimeSeconds,
        int TotalHitsLogged,
        int TotalPasses,
        double PassRatePercent
    );

    public record PagedResult<T>(
        IReadOnlyList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        PlayFilterSummary Summary
    )
    {
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
        public bool HasNextPage => PageNumber < TotalPages;
        public bool HasPreviousPage => PageNumber > 1;
    }
}
