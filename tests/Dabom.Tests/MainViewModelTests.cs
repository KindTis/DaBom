using Dabom.Library;
using Dabom.Main;
using System.IO;

namespace Dabom.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task Search_ClearsExcludedSelectionButKeepsIncludedSelection()
    {
        var root = Directory.CreateTempSubdirectory("dabom-vm-");
        try
        {
            var first = Path.Combine(root.FullName, "Alpha.mkv");
            var second = Path.Combine(root.FullName, "Beta.mkv");
            var scanner = new StubScanner(first, second);
            var vm = CreateViewModel(
                new LibraryStore(root.FullName), scanner,
                CachedData(root.FullName, first, second));
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single(video => video.Path == first);

            vm.SearchText = "Alpha";
            Assert.IsNotNull(vm.SelectedVideo);

            vm.SearchText = "Beta";
            Assert.IsNull(vm.SelectedVideo);

            vm.SearchText = string.Empty;
            Assert.IsNull(vm.SelectedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task AddLocation_WhenSaveFails_KeepsPreviousLocations()
    {
        var root = Directory.CreateTempSubdirectory("dabom-location-");
        try
        {
            var store = new LibraryStore(root.FullName, (_, _, _) => throw new IOException("disk full"));
            var vm = CreateViewModel(
                store, new StubScanner(), new LibraryData { Locations = [@"D:\Old"] });

            var saved = await vm.AddLocationAsync(@"D:\New");

            Assert.IsFalse(saved);
            CollectionAssert.AreEqual(new[] { @"D:\Old" }, vm.Locations.ToArray());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LocationChanges_WithUnchangedFileCache_PersistAcrossReload()
    {
        var root = Directory.CreateTempSubdirectory("dabom-location-reload-");
        try
        {
            var location = Directory.CreateDirectory(Path.Combine(root.FullName, "Movies")).FullName;
            var scanner = new StubScanner();
            var vm = CreateViewModel(new LibraryStore(root.FullName), scanner, new LibraryData());

            Assert.IsTrue(await vm.AddLocationAsync(location));
            var afterAdd = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { location }, afterAdd.Locations);

            Assert.IsTrue(await vm.RemoveLocationAsync(location));
            var afterRemove = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.AreEqual(0, afterRemove.Locations.Length);
            Assert.AreEqual(2, scanner.Calls);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void VideoItem_WhenPosterFileIsMissing_UsesNoPosterState()
    {
        var root = Directory.CreateTempSubdirectory("dabom-missing-poster-");
        try
        {
            var item = new VideoItemViewModel(
                @"D:\Movie.mkv",
                new VideoRecord { Poster = "posters/missing.jpg" },
                new LibraryStore(root.FullName));

            Assert.IsNull(item.Poster);
            Assert.IsFalse(item.HasPoster);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_DefaultsToTitleAscending()
    {
        var root = Directory.CreateTempSubdirectory("dabom-sort-");
        try
        {
            var zulu = Path.Combine(root.FullName, "Zulu.mkv");
            var alpha = Path.Combine(root.FullName, "Alpha.mkv");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(zulu, alpha),
                CachedData(root.FullName, zulu, alpha));

            await vm.ScanAsync();

            var titles = vm.VisibleVideos.Cast<VideoItemViewModel>()
                .Select(video => video.DisplayTitle)
                .ToArray();
            Assert.AreEqual(2, titles.Length, $"실제 제목: {string.Join(", ", titles)}; 상태: {vm.StatusMessage}");
            CollectionAssert.AreEqual(new[] { "Alpha", "Zulu" }, titles);
            Assert.AreEqual(VideoSort.Title, vm.SelectedSort);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LocationChange_RejectsConcurrentMutationUntilSaveAndScanFinish()
    {
        var root = Directory.CreateTempSubdirectory("dabom-location-lock-");
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commits = 0;
            var store = new LibraryStore(root.FullName, async (temporary, destination, _) =>
            {
                commits++;
                started.TrySetResult();
                await release.Task;
                File.Move(temporary, destination);
            });
            var scanner = new StubScanner();
            var vm = CreateViewModel(store, scanner, new LibraryData());

            var first = vm.AddLocationAsync(Path.Combine(root.FullName, "First"));
            await started.Task;

            Assert.IsFalse(vm.CanMutateLibrary);
            Assert.IsFalse(await vm.AddLocationAsync(Path.Combine(root.FullName, "Second")));
            Assert.IsFalse(await vm.RemoveLocationAsync(Path.Combine(root.FullName, "First")));
            Assert.AreEqual(1, commits);

            release.TrySetResult();
            Assert.IsTrue(await first);
            Assert.AreEqual(1, scanner.Calls);
            Assert.IsTrue(vm.CanMutateLibrary);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task StoreReadFailure_DisablesMutationAndPreservesLoadWarning()
    {
        var root = Directory.CreateTempSubdirectory("dabom-disabled-");
        try
        {
            var working = new LibraryStore(root.FullName);
            await working.SaveAsync(new LibraryData { Locations = [@"D:\Movies"] });
            var jsonPath = Path.Combine(root.FullName, "library.json");
            var store = new LibraryStore(root.FullName);
            var scanner = new StubScanner();

            using (new FileStream(jsonPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var data = await store.LoadAsync(CancellationToken.None);
                var vm = CreateViewModel(store, scanner, data);

                Assert.IsFalse(vm.CanMutateLibrary);
                Assert.IsFalse(await vm.AddLocationAsync(root.FullName));
                Assert.AreEqual(0, scanner.Calls);
                StringAssert.Contains(vm.StatusMessage, jsonPath);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task RemoveLocation_PreservesVideoRecordPosterAndOriginalFile()
    {
        var root = Directory.CreateTempSubdirectory("dabom-location-preserve-");
        try
        {
            var location = Directory.CreateDirectory(Path.Combine(root.FullName, "Movies")).FullName;
            var videoPath = Path.Combine(location, "Movie.mkv");
            await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
            var dataRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var posters = Directory.CreateDirectory(Path.Combine(dataRoot, "posters"));
            var posterPath = Path.Combine(posters.FullName, "poster.jpg");
            await File.WriteAllBytesAsync(posterPath, [4, 5, 6]);
            var data = new LibraryData
            {
                Locations = [location],
                VideosByPath = new(StringComparer.OrdinalIgnoreCase)
                {
                    [videoPath] = new() { Title = "보존", Poster = "posters/poster.jpg" }
                }
            };
            var store = new LibraryStore(dataRoot);
            await store.SaveAsync(data);
            var vm = CreateViewModel(store, new StubScanner(), data);

            Assert.IsTrue(await vm.RemoveLocationAsync(location));

            var reloaded = await new LibraryStore(dataRoot).LoadAsync(CancellationToken.None);
            Assert.IsTrue(reloaded.VideosByPath.ContainsKey(Path.GetFullPath(videoPath)));
            Assert.IsTrue(File.Exists(videoPath));
            Assert.IsTrue(File.Exists(posterPath));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_ReusesVideoItemAndReplacesWarnings()
    {
        var root = Directory.CreateTempSubdirectory("dabom-scan-merge-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var scanner = new SequenceScanner(
                Result(path, 1, new ScanWarning(path, "첫 경고")),
                Result(path, 1, new ScanWarning(path, "새 경고")));
            var now = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
            var vm = new MainViewModel(
                new LibraryStore(root.FullName), scanner,
                CachedData(root.FullName, path),
                _ => true, () => now, _ => 0);

            await vm.ScanAsync();
            var item = vm.Videos.Single();
            await vm.ScanAsync();

            Assert.AreSame(item, vm.Videos.Single());
            Assert.AreEqual(1, item.Record.FileSizeBytes);
            Assert.AreEqual("새 경고", vm.Warnings.Single().Reason);
            Assert.AreEqual(now, vm.LastScanUtc);
            Assert.AreSame(item, vm.FeaturedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static MainViewModel CreateViewModel(
        LibraryStore store,
        ILibraryScanner scanner,
        LibraryData data) =>
        new(store, scanner, data, _ => true, () => DateTimeOffset.UtcNow, _ => 0);

    private static LibraryData CachedData(string location, params string[] paths) => new()
    {
        Locations = [location],
        VideosByPath = paths.ToDictionary(
            Path.GetFullPath,
            path => new VideoRecord
            {
                FileSizeBytes = 1,
                LastWriteTimeUtc = DateTimeOffset.UnixEpoch
            },
            StringComparer.OrdinalIgnoreCase)
    };

    private static ScanResult Result(string path, long size, ScanWarning warning)
    {
        var fullPath = Path.GetFullPath(path);
        return new(
            new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase)
            {
                [fullPath] = new(fullPath, size, DateTimeOffset.UnixEpoch, null)
            },
            [warning]);
    }

    private sealed class StubScanner(params string[] paths) : ILibraryScanner
    {
        public int Calls { get; private set; }

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken)
        {
            Calls++;
            var videos = paths.ToDictionary(
                Path.GetFullPath,
                path => new ScannedVideo(Path.GetFullPath(path), 1, DateTimeOffset.UnixEpoch, null),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<ScanResult>(new(videos, []));
        }
    }

    private sealed class SequenceScanner(params ScanResult[] results) : ILibraryScanner
    {
        private readonly Queue<ScanResult> _results = new(results);

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken) =>
            Task.FromResult(_results.Dequeue());
    }
}
