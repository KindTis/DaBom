using Dabom.Library;
using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class LibraryScannerTests
{
    [TestMethod]
    public async Task ScanAsync_FindsSupportedNestedFilesOnce()
    {
        var root = Directory.CreateTempSubdirectory("dabom-scan-");
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "Nested"));
            var video = Path.Combine(nested.FullName, "Movie.MKV");
            await File.WriteAllBytesAsync(video, [1, 2, 3]);
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "notes.txt"), "ignore");
            var scanner = new LibraryScanner(_ => 1234L);

            var result = await scanner.ScanAsync(
                [root.FullName, nested.FullName, root.FullName.ToUpperInvariant()],
                new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase),
                null,
                CancellationToken.None);

            Assert.AreEqual(1, result.Videos.Count);
            Assert.AreEqual(1234L, result.Videos[Path.GetFullPath(video)].DurationTicks);
            Assert.AreEqual(0, result.Warnings.Count);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_ReusesDurationOnlyWhenSizeAndTimestampMatch()
    {
        var root = Directory.CreateTempSubdirectory("dabom-cache-");
        try
        {
            var video = Path.Combine(root.FullName, "Movie.mp4");
            await File.WriteAllBytesAsync(video, [1, 2, 3]);
            var info = new FileInfo(video);
            var reads = 0;
            var scanner = new LibraryScanner(_ => { reads++; return 9000L; });
            var cache = new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(video)] = new()
                {
                    FileSizeBytes = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    DurationTicks = 5000L
                }
            };

            var unchanged = await scanner.ScanAsync([root.FullName], cache, null, CancellationToken.None);
            Assert.AreEqual(5000L, unchanged.Videos[Path.GetFullPath(video)].DurationTicks);
            Assert.AreEqual(0, reads);

            await File.AppendAllTextAsync(video, "changed");
            var changed = await scanner.ScanAsync([root.FullName], cache, null, CancellationToken.None);
            Assert.AreEqual(9000L, changed.Videos[Path.GetFullPath(video)].DurationTicks);
            Assert.AreEqual(1, reads);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenOnlyTimestampChanges_RereadsDuration()
    {
        var root = Directory.CreateTempSubdirectory("dabom-cache-time-");
        try
        {
            var video = Path.Combine(root.FullName, "Movie.mp4");
            await File.WriteAllBytesAsync(video, [1, 2, 3]);
            var original = new FileInfo(video);
            var cache = new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.GetFullPath(video)] = new()
                {
                    FileSizeBytes = original.Length,
                    LastWriteTimeUtc = original.LastWriteTimeUtc,
                    DurationTicks = 5000L
                }
            };
            var reads = 0;
            var scanner = new LibraryScanner(_ => { reads++; return 9000L; });

            File.SetLastWriteTimeUtc(video, original.LastWriteTimeUtc.AddMinutes(-5));
            var changed = new FileInfo(video);
            Assert.AreEqual(original.Length, changed.Length);
            Assert.AreNotEqual(original.LastWriteTimeUtc, changed.LastWriteTimeUtc);

            var result = await scanner.ScanAsync([root.FullName], cache, null, CancellationToken.None);

            Assert.AreEqual(9000L, result.Videos[Path.GetFullPath(video)].DurationTicks);
            Assert.AreEqual(1, reads);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenOneRootIsDenied_ReportsPermissionAndContinues()
    {
        var root = Directory.CreateTempSubdirectory("dabom-permission-");
        try
        {
            var video = Path.Combine(root.FullName, "Movie.mp4");
            await File.WriteAllBytesAsync(video, [1]);
            var denied = Path.GetFullPath(Path.Combine(root.FullName, "Denied"));
            var scanner = new LibraryScanner(
                _ => null,
                path => path.Equals(denied, StringComparison.OrdinalIgnoreCase)
                    ? throw new UnauthorizedAccessException()
                    : File.GetAttributes(path));

            var result = await scanner.ScanAsync(
                [denied, root.FullName],
                new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase),
                null,
                CancellationToken.None);

            Assert.AreEqual(1, result.Videos.Count);
            Assert.AreEqual(denied, result.Warnings.Single().Path);
            Assert.AreEqual("접근 권한 없음", result.Warnings.Single().Reason);
            CollectionAssert.AreEqual(new[] { denied }, result.UnavailablePaths.ToArray());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenDurationReaderThrows_IncludesVideoAndContinues()
    {
        var root = Directory.CreateTempSubdirectory("dabom-duration-failure-");
        try
        {
            var failed = Path.Combine(root.FullName, "Failed.mp4");
            var succeeded = Path.Combine(root.FullName, "Succeeded.mkv");
            await File.WriteAllBytesAsync(failed, [1]);
            await File.WriteAllBytesAsync(succeeded, [2]);
            var scanner = new LibraryScanner(path =>
                Path.GetFileName(path).StartsWith("Failed", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("handler failed")
                    : 7000L);

            var result = await scanner.ScanAsync(
                [root.FullName],
                new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase),
                null,
                CancellationToken.None);

            Assert.AreEqual(2, result.Videos.Count);
            Assert.IsNull(result.Videos[Path.GetFullPath(failed)].DurationTicks);
            Assert.AreEqual(7000L, result.Videos[Path.GetFullPath(succeeded)].DurationTicks);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_ReportsEachNewVideoCountInOrder()
    {
        var root = Directory.CreateTempSubdirectory("dabom-scan-progress-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "A.mp4"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "B.mkv"), [2]);
            var counts = new List<int>();
            var scanner = new LibraryScanner(_ => null);

            var result = await scanner.ScanAsync(
                [root.FullName],
                new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase),
                new InlineProgress<int>(counts.Add),
                CancellationToken.None);

            Assert.AreEqual(2, result.Videos.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, counts);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
