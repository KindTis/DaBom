namespace Dabom.Library;

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
    IReadOnlyList<ScanWarning> Warnings);

public enum VideoSort
{
    Title,
    ReleaseDate,
    FileModified
}
