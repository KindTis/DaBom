using System.IO;

namespace Dabom.Library;

public sealed class LibraryScanner : ILibraryScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".m2ts"],
        StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, long?> _readDuration;
    private readonly Func<string, FileAttributes> _getAttributes;

    public LibraryScanner() : this(
        WindowsDurationReader.TryReadTicks,
        File.GetAttributes) { }

    internal LibraryScanner(
        Func<string, long?> readDuration,
        Func<string, FileAttributes>? getAttributes = null)
    {
        _readDuration = readDuration;
        _getAttributes = getAttributes ?? File.GetAttributes;
    }

    public Task<ScanResult> ScanAsync(
        IReadOnlyList<string> locations,
        IReadOnlyDictionary<string, VideoRecord> existingFileCache,
        IProgress<int>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(locations, existingFileCache, progress, cancellationToken), cancellationToken);

    private ScanResult Scan(
        IReadOnlyList<string> locations,
        IReadOnlyDictionary<string, VideoRecord> cache,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var videos = new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<ScanWarning>();
        var unavailablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLocation in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string location;
            try
            {
                location = Path.GetFullPath(rawLocation);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings.Add(new(rawLocation, "올바르지 않은 경로"));
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(location);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visitedDirectories.Add(directory))
                {
                    continue;
                }

                try
                {
                    if ((_getAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        pending.Push(Path.GetFullPath(child));
                    }

                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!SupportedExtensions.Contains(Path.GetExtension(file)))
                        {
                            continue;
                        }

                        string? path = null;
                        try
                        {
                            path = Path.GetFullPath(file);
                            if (videos.ContainsKey(path))
                            {
                                continue;
                            }

                            var info = new FileInfo(path);
                            var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                            cache.TryGetValue(path, out var old);
                            var duration = old is not null
                                && old.FileSizeBytes == info.Length
                                && old.LastWriteTimeUtc == modified
                                    ? old.DurationTicks
                                    : ReadDurationWithoutFailingScan(path);
                            videos[path] = new(path, info.Length, modified, duration);
                            progress?.Report(videos.Count);
                        }
                        catch (Exception error) when (IsRecoverable(error))
                        {
                            warnings.Add(new(file, ShortReason(error)));
                            if (path is not null) unavailablePaths.Add(path);
                        }
                    }
                }
                catch (Exception error) when (IsRecoverable(error))
                {
                    warnings.Add(new(directory, ShortReason(error)));
                    unavailablePaths.Add(directory);
                }
            }
        }

        return new(videos, warnings) { UnavailablePaths = unavailablePaths.ToArray() };
    }

    private long? ReadDurationWithoutFailingScan(string path)
    {
        try { return _readDuration(path); }
        catch { return null; }
    }

    private static bool IsRecoverable(Exception error) =>
        error is UnauthorizedAccessException or IOException;

    private static string ShortReason(Exception error) => error switch
    {
        UnauthorizedAccessException => "접근 권한 없음",
        DirectoryNotFoundException or FileNotFoundException => "경로를 찾을 수 없음",
        PathTooLongException => "경로가 너무 김",
        _ => "입출력 오류"
    };
}
