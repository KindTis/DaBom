using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.IO;
using System.Windows.Input;

namespace Dabom.Tests;

[TestClass]
public sealed class CommandStateTests
{
    [TestMethod]
    public async Task ScanAsync_DisablesMutatingCommandsUntilCompletion()
    {
        var root = Directory.CreateTempSubdirectory("dabom-command-scan-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var scanner = new BlockingSecondScan(path);
            var vm = CreateViewModel(new LibraryStore(root.FullName), scanner, data);
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single();

            var scan = vm.ScanAsync();
            await scanner.Started.Task;

            Assert.IsTrue(vm.IsScanning);
            Assert.IsFalse(vm.RescanCommand.CanExecute(null));
            Assert.IsFalse(vm.PlayCommand.CanExecute(null));
            Assert.IsFalse(vm.PlayFeaturedCommand.CanExecute(null));
            Assert.IsFalse(vm.OpenMetadataCommand.CanExecute(null));
            Assert.IsFalse(vm.RemoveLocationCommand.CanExecute(root.FullName));

            scanner.Complete();
            await scan;

            Assert.IsFalse(vm.IsScanning);
            Assert.IsTrue(vm.RescanCommand.CanExecute(null));
            Assert.IsTrue(vm.PlayCommand.CanExecute(null));
            Assert.IsTrue(vm.PlayFeaturedCommand.CanExecute(null));
            Assert.IsTrue(vm.OpenMetadataCommand.CanExecute(null));
            Assert.IsTrue(vm.RemoveLocationCommand.CanExecute(root.FullName));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LocationSave_DisablesMutatingCommandsUntilFailureCompletes()
    {
        var root = Directory.CreateTempSubdirectory("dabom-command-location-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new LibraryStore(root.FullName, async (_, _, _) =>
            {
                started.TrySetResult();
                await release.Task;
                throw new IOException("disk full");
            });
            var vm = CreateViewModel(store, new ImmediateScanner(path), data);
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single();

            var change = vm.AddLocationAsync(Path.Combine(root.FullName, "Other"));
            await started.Task;

            Assert.IsTrue(vm.IsChangingLocations);
            AssertCommandsDisabled(vm, root.FullName);

            release.TrySetResult();
            Assert.IsFalse(await change);
            Assert.IsFalse(vm.IsChangingLocations);
            Assert.IsTrue(vm.RescanCommand.CanExecute(null));
            Assert.IsTrue(vm.PlayCommand.CanExecute(null));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task PlaybackSave_DisablesMutatingCommandsUntilCompletion()
    {
        var root = Directory.CreateTempSubdirectory("dabom-command-playback-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            await new LibraryStore(root.FullName).SaveAsync(data);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new LibraryStore(root.FullName, async (temporary, destination, _) =>
            {
                started.TrySetResult();
                await release.Task;
                File.Replace(temporary, destination, null);
            });
            var vm = CreateViewModel(store, new ImmediateScanner(path), data);
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single();

            var play = vm.PlayAsync(vm.SelectedVideo);
            await started.Task;

            Assert.IsTrue(vm.IsRecordingPlayback);
            AssertCommandsDisabled(vm, root.FullName);

            release.TrySetResult();
            await play;
            Assert.IsFalse(vm.IsRecordingPlayback);
            Assert.IsTrue(vm.RescanCommand.CanExecute(null));
            Assert.IsTrue(vm.PlayCommand.CanExecute(null));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task StoreReadFailure_DisablesCommandsAndKeepsWarningWhenSelectionIsMissing()
    {
        var root = Directory.CreateTempSubdirectory("dabom-command-disabled-");
        try
        {
            var working = new LibraryStore(root.FullName);
            await working.SaveAsync(new LibraryData());
            var jsonPath = Path.Combine(root.FullName, "library.json");
            var store = new LibraryStore(root.FullName);

            using (new FileStream(jsonPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var data = await store.LoadAsync(CancellationToken.None);
                var vm = CreateViewModel(store, new ImmediateScanner(), data);
                var warning = vm.StatusMessage;

                AssertCommandsDisabled(vm, root.FullName);
                vm.NotifyMissingSelection();

                Assert.AreEqual(warning, vm.StatusMessage);
                StringAssert.Contains(vm.StatusMessage, jsonPath);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task OpenMetadataCommand_UsesICommandAndRaisesSelectedDraft()
    {
        var root = Directory.CreateTempSubdirectory("dabom-command-metadata-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var vm = CreateViewModel(
                new LibraryStore(root.FullName), new ImmediateScanner(path), data);
            await vm.ScanAsync();
            Assert.IsFalse(vm.OpenMetadataCommand.CanExecute(null));
            vm.NotifyMissingSelection();
            Assert.AreEqual("먼저 영상을 선택하세요", vm.StatusMessage);
            vm.SelectedVideo = vm.Videos.Single();
            MetadataEditorViewModel? requested = null;
            vm.MetadataEditRequested += (_, editor) => requested = editor;

            vm.OpenMetadataCommand.Execute(null);

            Assert.IsNotNull(requested);
            Assert.AreEqual(path, requested.Path);
            foreach (var name in new[]
            {
                nameof(MainViewModel.RescanCommand),
                nameof(MainViewModel.PlayCommand),
                nameof(MainViewModel.PlayFeaturedCommand),
                nameof(MainViewModel.OpenMetadataCommand),
                nameof(MainViewModel.RemoveLocationCommand)
            })
            {
                Assert.AreEqual(typeof(ICommand), typeof(MainViewModel).GetProperty(name)!.PropertyType);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static void AssertCommandsDisabled(MainViewModel vm, string location)
    {
        Assert.IsFalse(vm.RescanCommand.CanExecute(null));
        Assert.IsFalse(vm.PlayCommand.CanExecute(null));
        Assert.IsFalse(vm.PlayFeaturedCommand.CanExecute(null));
        Assert.IsFalse(vm.OpenMetadataCommand.CanExecute(null));
        Assert.IsFalse(vm.RemoveLocationCommand.CanExecute(location));
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

    private sealed class ImmediateScanner(params string[] paths) : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken) => Task.FromResult(Result(paths));
    }

    private sealed class BlockingSecondScan(string path) : ILibraryScanner
    {
        private readonly TaskCompletionSource<ScanResult> _completion = new();
        private int _calls;
        public TaskCompletionSource Started { get; } = new();

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken)
        {
            if (++_calls == 1) return Task.FromResult(Result([path]));
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete() => _completion.SetResult(Result([path]));
    }

    private static ScanResult Result(IEnumerable<string> paths)
    {
        var videos = paths.ToDictionary(
            Path.GetFullPath,
            path => new ScannedVideo(
                Path.GetFullPath(path), 1, DateTimeOffset.UnixEpoch, null),
            StringComparer.OrdinalIgnoreCase);
        return new(videos, []);
    }
}
