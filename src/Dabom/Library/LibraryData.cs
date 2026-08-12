namespace Dabom.Library;

public enum MediaType
{
    Unknown,
    Movie,
    TvEpisode
}

public enum MetadataStatus
{
    Unspecified = 0,
    Pending,
    Matched,
    NotFound,
    Failed,
    Manual
}

public enum VideoFileStatus
{
    Unknown,
    Present,
    Missing,
    Unavailable
}

public enum MetadataField
{
    Title,
    OriginalTitle,
    SeriesTitle,
    EpisodeTitle,
    ReleaseDate,
    Genres,
    Director,
    Actors,
    Synopsis,
    Poster,
    MediaType,
    SeasonNumber,
    EpisodeNumber
}

public sealed record ProviderReference(
    string ProviderKey,
    string ResourceType,
    string ResourceId);

public sealed record LibraryData
{
    public string[] Locations { get; init; } = [];
    public Dictionary<string, VideoRecord> VideosByPath { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record VideoRecord
{
    public string? Title { get; init; }
    public string? OriginalTitle { get; init; }
    public DateOnly? ReleaseDate { get; init; }
    public string? Director { get; init; }
    public string[] Actors { get; init; } = [];
    public string? Synopsis { get; init; }
    public string? Poster { get; init; }
    public MediaType MediaType { get; init; }
    public string? SeriesTitle { get; init; }
    public string? EpisodeTitle { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public string[] Genres { get; init; } = [];
    public MetadataStatus MetadataStatus { get; init; }
    public ProviderReference[] ProviderReferences { get; init; } = [];
    public HashSet<MetadataField> UserEditedFields { get; init; } = [];
    public long FileSizeBytes { get; init; }
    public DateTimeOffset LastWriteTimeUtc { get; init; }
    public long? DurationTicks { get; init; }
    public DateTimeOffset? LastPlayedUtc { get; init; }
}

public sealed record ScannedVideo(
    string Path,
    long FileSizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    long? DurationTicks);

public sealed record ScanWarning(string Path, string Reason)
{
    public string DisplayText => $"{Path} — {Reason}";
}

public sealed record ScanResult(
    IReadOnlyDictionary<string, ScannedVideo> Videos,
    IReadOnlyList<ScanWarning> Warnings)
{
    public IReadOnlyList<string> UnavailablePaths { get; init; } = [];
}

public enum VideoSort
{
    Title,
    ReleaseDate,
    FileModified
}
