using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace Dabom.Tests;

[TestClass]
public sealed class CommandStateTests
{
    [TestMethod]
    public void ScanAsync_DisablesMutatingCommandsUntilCompletion()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-command-scan-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var newPath = Path.Combine(root.FullName, "New.Movie.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    MetadataStatus = MetadataStatus.Matched
                };
                var scanner = new ExpandingScanner(path, newPath);
                var provider = new BlockingMetadataProvider();
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var enrichment = new MetadataEnrichmentService(
                    new MediaFilenameParser(),
                    [provider],
                    store,
                    imageClient);
                var vm = new MainViewModel(
                    store,
                    scanner,
                    data,
                    _ => true,
                    () => DateTimeOffset.UtcNow,
                    _ => 0,
                    enrichment);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();

                var scan = vm.ScanAsync();
                var startSignal = await Task.WhenAny(
                    provider.Started.Task,
                    Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.AreSame(
                    provider.Started.Task,
                    startSignal,
                    $"조정 서비스가 시작되지 않았습니다. 스캔 {scanner.Calls}회, 검색 {provider.SearchCalls}회, 상태: {vm.StatusMessage}");

                Assert.IsTrue(vm.IsScanning);
                Assert.IsFalse(vm.RescanCommand.CanExecute(null));
                Assert.IsFalse(vm.PlayCommand.CanExecute(null));
                Assert.IsFalse(vm.PlayFeaturedCommand.CanExecute(null));
                Assert.IsFalse(vm.OpenMetadataCommand.CanExecute(null));
                Assert.IsFalse(vm.RemoveLocationCommand.CanExecute(root.FullName));
                vm.SearchText = "Movie";
                vm.SelectedSort = VideoSort.FileModified;
                vm.SelectedVideo = vm.Videos.Single(video => video.Path == path);
                Assert.AreEqual("Movie", vm.SearchText);
                Assert.AreEqual(VideoSort.FileModified, vm.SelectedSort);
                Assert.AreEqual(path, vm.SelectedVideo.Path);

                provider.Complete();
                var completionSignal = await Task.WhenAny(
                    scan,
                    Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.AreSame(
                    scan,
                    completionSignal,
                    $"조정 서비스 완료 뒤 스캔이 끝나지 않았습니다. 상태: {vm.StatusMessage}");
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
        });
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
    public void DeletionSave_DisablesMutatingCommandsUntilCompletion()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-command-delete-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                var started = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var store = new LibraryStore(
                    root.FullName,
                    async (temporary, destination, _) =>
                    {
                        started.TrySetResult();
                        await release.Task;
                        File.Move(temporary, destination, true);
                    });
                var identity = new FileIdentity(7, 10, 20);
                var vm = new MainViewModel(
                    store,
                    new ImmediateScanner(path),
                    data,
                    _ => true,
                    () => DateTimeOffset.UtcNow,
                    _ => 0,
                    null,
                    _ => new(VideoFileStatus.Present, identity),
                    _ => { });
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var request = vm.PrepareVideoDeletion();

                var deletion = vm.DeleteVideoAsync(request!);
                await started.Task;

                Assert.IsTrue(vm.IsDeleting);
                Assert.IsFalse(vm.CanMutateLibrary);
                AssertCommandsDisabled(vm, root.FullName);

                release.TrySetResult();
                Assert.IsTrue(await deletion);
                Assert.IsFalse(vm.IsDeleting);
                Assert.IsTrue(vm.CanMutateLibrary);
                Assert.IsTrue(vm.RescanCommand.CanExecute(null));
                Assert.IsTrue(vm.RemoveLocationCommand.CanExecute(root.FullName));
            }
            finally
            {
                root.Delete(true);
            }
        });
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

    private static void RunOnDispatcher(Func<Task> action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        Exception? failure = null;

        async void Run()
        {
            try
            {
                await action();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                frame.Continue = false;
            }
        }

        dispatcher.BeginInvoke((Action)Run);
        Dispatcher.PushFrame(frame);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
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

    private sealed class ImmediateScanner(params string[] paths) : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken) => Task.FromResult(Result(paths, progress));
    }

    private sealed class ExpandingScanner(
        string existingPath,
        string newPath) : ILibraryScanner
    {
        private int _calls;
        public int Calls => _calls;

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(++_calls == 1
                ? Result([existingPath], progress)
                : Result([existingPath, newPath], progress));
        }
    }

    private sealed class BlockingMetadataProvider : IMetadataProvider
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProviderKey => "test";
        public int SearchCalls { get; private set; }

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
            MetadataQuery query,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("test", "movie", "1", MediaType.Movie)
            ]);
        }

        public async Task<MetadataDetails> GetDetailsAsync(
            MetadataCandidate candidate,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _release.Task;
            return new(
                MediaType: MediaType.Movie,
                Title: "새 영화",
                OriginalTitle: "New Movie",
                SeriesTitle: null,
                EpisodeTitle: null,
                ReleaseDate: new DateOnly(2024, 1, 1),
                Genres: [],
                Director: null,
                Actors: [],
                Synopsis: null,
                SeasonNumber: null,
                EpisodeNumber: null,
                PosterUri: null,
                ProviderReferences: [new("test", "movie", "1")]);
        }

        public void Complete() => _release.TrySetResult();
    }

    private static ScanResult Result(IEnumerable<string> paths, IProgress<int>? progress = null)
    {
        var videos = new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            videos[fullPath] = new(fullPath, 1, DateTimeOffset.UnixEpoch, null);
            progress?.Report(videos.Count);
        }
        return new(videos, []);
    }
}
