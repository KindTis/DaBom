namespace Dabom.Library;

public interface ILibraryScanner
{
    Task<ScanResult> ScanAsync(
        IReadOnlyList<string> locations,
        IReadOnlyDictionary<string, VideoRecord> existingFileCache,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}
