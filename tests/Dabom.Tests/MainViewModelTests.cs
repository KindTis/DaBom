using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
    public void VideoItem_FormatsFullKoreanReleaseDateForReferenceTooltip()
    {
        var root = Directory.CreateTempSubdirectory("dabom-release-date-");
        try
        {
            var item = new VideoItemViewModel(
                @"D:\Movie.mkv",
                new VideoRecord { ReleaseDate = new DateOnly(2024, 2, 28) },
                new LibraryStore(root.FullName));

            Assert.AreEqual("2024년 2월 28일", item.ReleaseDateText);
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

    [TestMethod]
    public async Task ScanAsync_CreatesPendingRecordWithFileNameTitle()
    {
        var root = Directory.CreateTempSubdirectory("dabom-pending-");
        try
        {
            var path = Path.Combine(root.FullName, "New.Movie.2024.mkv");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(path),
                new LibraryData { Locations = [root.FullName] });

            await vm.ScanAsync();

            Assert.AreEqual(
                Path.GetFileNameWithoutExtension(path),
                vm.Videos.Single().Record.Title);
            Assert.AreEqual(
                MetadataStatus.Pending,
                vm.Videos.Single().Record.MetadataStatus);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void ScanAsync_AfterScanEnrichesPendingAndFailedRecordsSequentially()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-scan-enrich-");
            try
            {
                var first = Path.Combine(root.FullName, "First.Movie.mkv");
                var second = Path.Combine(root.FullName, "Second.Movie.mkv");
                var data = CachedData(root.FullName, first);
                data.VideosByPath[first] = data.VideosByPath[first] with
                {
                    Title = "First Movie",
                    MetadataStatus = MetadataStatus.Failed
                };
                var queries = new List<string>();
                var provider = new TestProvider(
                    (query, _) =>
                    {
                        queries.Add(query.Title);
                        return Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [
                            new("test", "movie", query.Title, MediaType.Movie)
                        ]);
                    },
                    (candidate, _) => Task.FromResult(
                        MovieDetails($"적용 {candidate.ResourceId}", candidate.ResourceId)));
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(first, second),
                    CreateEnrichment(store, imageClient, provider),
                    data);

                await vm.ScanAsync();

                CollectionAssert.AreEqual(
                    new[] { "First Movie", "Second Movie" },
                    queries);
                Assert.IsTrue(vm.Videos.All(video =>
                    video.Record.MetadataStatus == MetadataStatus.Matched));
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task ScanAsync_UpdatesVideoItemOnlyAfterEachMetadataSave()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-save-order-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Title = "이전 제목",
                MetadataStatus = MetadataStatus.Pending
            };
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
                    File.Move(temporary, destination);
                });
            var provider = TestProvider.ForMovie(
                MovieDetails("새 제목", "1"));
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);

            var scan = vm.ScanAsync();
            await started.Task;

            Assert.AreEqual("이전 제목", vm.Videos.Single().Record.Title);
            Assert.AreEqual(
                MetadataStatus.Pending,
                vm.Videos.Single().Record.MetadataStatus);

            release.TrySetResult();
            await scan;

            Assert.AreEqual("새 제목", vm.Videos.Single().Record.Title);
            Assert.AreEqual(
                MetadataStatus.Matched,
                vm.Videos.Single().Record.MetadataStatus);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenOneMetadataCommitFails_ContinuesWithNextVideo()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-save-failure-");
        try
        {
            var first = Path.Combine(root.FullName, "First.Movie.mkv");
            var second = Path.Combine(root.FullName, "Second.Movie.mkv");
            var data = CachedData(root.FullName, first, second);
            data.VideosByPath[first] = data.VideosByPath[first] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            data.VideosByPath[second] = data.VideosByPath[second] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            var commits = 0;
            var store = new LibraryStore(
                root.FullName,
                (temporary, destination, _) =>
                {
                    commits++;
                    if (commits == 1) throw new IOException("disk full");
                    File.Move(temporary, destination);
                    return Task.CompletedTask;
                });
            var provider = new TestProvider(
                (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                [
                    new("test", "movie", query.Title, MediaType.Movie)
                ]),
                (candidate, _) => Task.FromResult(
                    MovieDetails(candidate.ResourceId, candidate.ResourceId)));
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(first, second),
                CreateEnrichment(store, imageClient, provider),
                data);

            await vm.ScanAsync();

            Assert.AreEqual(2, commits);
            Assert.AreEqual(
                MetadataStatus.Pending,
                vm.Videos.Single(video => video.Path == first)
                    .Record.MetadataStatus);
            Assert.AreEqual(
                MetadataStatus.Matched,
                vm.Videos.Single(video => video.Path == second)
                    .Record.MetadataStatus);
            StringAssert.Contains(vm.StatusMessage, "실패 1");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_ShowsProgressAndFinalMatchedNotFoundFailedCounts()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-progress-");
        try
        {
            var paths = new[]
            {
                Path.Combine(root.FullName, "Matched.mkv"),
                Path.Combine(root.FullName, "NotFound.mkv"),
                Path.Combine(root.FullName, "Failed.mkv")
            };
            var data = CachedData(root.FullName, paths);
            foreach (var path in paths)
            {
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    MetadataStatus = MetadataStatus.Pending
                };
            }
            var provider = new TestProvider(
                (query, _) => query.Title switch
                {
                    "Matched" => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                    [
                        new("test", "movie", "1", MediaType.Movie)
                    ]),
                    "NotFound" =>
                        Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
                    _ => Task.FromException<IReadOnlyList<MetadataCandidate>>(
                        new MetadataProviderException(
                            MetadataProviderFailureKind.InvalidResponse,
                            "bad response"))
                },
                (_, _) => Task.FromResult(MovieDetails("적용 제목", "1")));
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(paths),
                CreateEnrichment(store, imageClient, provider),
                data);
            var messages = new List<string>();
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.StatusMessage))
                {
                    messages.Add(vm.StatusMessage);
                }
            };

            await vm.ScanAsync();

            Assert.IsTrue(messages.Any(message =>
                message.StartsWith("메타데이터 처리 ", StringComparison.Ordinal)));
            Assert.AreEqual(
                "메타데이터 적용 완료 · 성공 1 · 결과 없음 1 · 실패 1",
                vm.StatusMessage);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenAuthenticationFails_PrioritizesEnvGuidance()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-auth-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            var provider = new TestProvider(
                (_, _) => Task.FromException<IReadOnlyList<MetadataCandidate>>(
                    new MetadataProviderException(
                        MetadataProviderFailureKind.Authentication,
                        "unauthorized")),
                (_, _) => throw new AssertFailedException());
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);

            await vm.ScanAsync();

            StringAssert.Contains(vm.StatusMessage, ".env");
            StringAssert.Contains(
                vm.StatusMessage,
                "DABOM_TMDB_ACCESS_TOKEN");
            Assert.IsFalse(vm.StatusMessage.Contains(
                "unauthorized",
                StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenEnrichedSelectedVideoLeavesFilter_ClearsSelection()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-filter-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Title = "찾는 제목",
                MetadataStatus = MetadataStatus.Pending
            };
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TestProvider(
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                [
                    new("test", "movie", "1", MediaType.Movie)
                ]),
                async (_, _) =>
                {
                    started.TrySetResult();
                    await release.Task;
                    return MovieDetails("다른 제목", "1");
                });
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);

            var scan = vm.ScanAsync();
            await started.Task;
            vm.SearchText = "찾는 제목";
            vm.SelectedVideo = vm.Videos.Single();
            release.TrySetResult();
            await scan;

            Assert.AreEqual(0, vm.VisibleCount);
            Assert.IsNull(vm.SelectedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task PlayAsync_WhenLaunchFails_DoesNotChangeHistory()
    {
        var root = Directory.CreateTempSubdirectory("dabom-play-launch-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var vm = new MainViewModel(
                new LibraryStore(root.FullName), new StubScanner(path), data,
                _ => false, () => DateTimeOffset.Parse("2026-07-18T12:00:00Z"), _ => 0);
            await vm.ScanAsync();
            var video = vm.Videos.Single();
            var featured = vm.FeaturedVideo;

            await vm.PlayAsync(video);

            Assert.IsNull(video.Record.LastPlayedUtc);
            Assert.AreSame(featured, vm.FeaturedVideo);
            StringAssert.Contains(vm.StatusMessage, "영상을 재생하지 못했습니다");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task PlayAsync_WhenHistorySaveFails_KeepsMemoryAndFeaturedState()
    {
        var root = Directory.CreateTempSubdirectory("dabom-play-save-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var store = new LibraryStore(
                root.FullName, (_, _, _) => throw new IOException("disk full"));
            var vm = new MainViewModel(
                store, new StubScanner(path), data,
                _ => true, () => DateTimeOffset.Parse("2026-07-18T12:00:00Z"), _ => 0);
            await vm.ScanAsync();
            var video = vm.Videos.Single();
            var featured = vm.FeaturedVideo;

            await vm.PlayAsync(video);

            Assert.IsNull(video.Record.LastPlayedUtc);
            Assert.AreSame(featured, vm.FeaturedVideo);
            StringAssert.Contains(vm.StatusMessage, "재생 이력 저장 실패");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task PlayAsync_RejectsConcurrentMutationsAndCommitsHistoryOnce()
    {
        var root = Directory.CreateTempSubdirectory("dabom-play-lock-");
        try
        {
            var firstPath = Path.Combine(root.FullName, "First.mkv");
            var secondPath = Path.Combine(root.FullName, "Second.mkv");
            var data = CachedData(root.FullName, firstPath, secondPath);
            await new LibraryStore(root.FullName).SaveAsync(data);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var commits = 0;
            var launches = 0;
            var scanner = new StubScanner(firstPath, secondPath);
            var store = new LibraryStore(root.FullName, async (temporary, destination, _) =>
            {
                commits++;
                started.TrySetResult();
                await release.Task;
                File.Replace(temporary, destination, null);
            });
            var playedAt = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
            var vm = new MainViewModel(
                store, scanner, data,
                _ => { launches++; return true; }, () => playedAt, _ => 0);
            await vm.ScanAsync();
            var firstVideo = vm.Videos.Single(video => video.Path == firstPath);
            var secondVideo = vm.Videos.Single(video => video.Path == secondPath);
            vm.SelectedVideo = firstVideo;
            var featured = vm.FeaturedVideo;

            var firstPlay = vm.PlayAsync(firstVideo);
            await started.Task;

            Assert.IsFalse(vm.CanMutateLibrary);
            await vm.PlayAsync(secondVideo);
            await vm.ScanAsync();
            Assert.IsFalse(await vm.AddLocationAsync(Path.Combine(root.FullName, "Other")));
            Assert.IsNull(vm.CreateMetadataEditor());
            Assert.AreEqual(1, launches);
            Assert.AreEqual(1, commits);
            Assert.AreEqual(1, scanner.Calls);

            release.TrySetResult();
            await firstPlay;

            Assert.IsTrue(vm.CanMutateLibrary);
            Assert.AreSame(featured, vm.FeaturedVideo);
            Assert.AreEqual(playedAt, firstVideo.Record.LastPlayedUtc);
            var reloaded = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.AreEqual(playedAt, reloaded.VideosByPath[firstPath].LastPlayedUtc);

            await vm.ScanAsync();
            Assert.AreSame(secondVideo, vm.FeaturedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void MetadataSave_WhenOldPosterCleanupFails_KeepsCommittedResult()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-metadata-cleanup-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var posters = Directory.CreateDirectory(Path.Combine(root.FullName, "posters"));
                var oldPoster = Path.Combine(posters.FullName, "old.png");
                WritePng(oldPoster);
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with { Poster = "posters/old.png" };
                await new LibraryStore(root.FullName).SaveAsync(data);
                var store = new LibraryStore(
                    root.FullName, deletePoster: _ => throw new UnauthorizedAccessException("locked"));
                var vm = CreateViewModel(store, new StubScanner(path), data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.MarkPosterRemoved();

                var saved = await editor.SaveAsync();

                Assert.IsTrue(saved);
                Assert.IsNull(vm.Videos.Single().Record.Poster);
                var reloaded = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
                Assert.IsNull(reloaded.VideosByPath[path].Poster);
                StringAssert.Contains(vm.StatusMessage, "이전 포스터를 정리하지 못했습니다");
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_WhenTitleLeavesActiveSearch_ClearsSelection()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-metadata-search-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with { Title = "찾는 제목" };
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName), new StubScanner(path), data);
                await vm.ScanAsync();
                vm.SearchText = "찾는 제목";
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.Title = "다른 제목";

                Assert.IsTrue(await editor.SaveAsync());

                Assert.AreEqual(0, vm.VisibleCount);
                Assert.IsNull(vm.SelectedVideo);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_ReappliesCurrentSort()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-metadata-sort-");
            try
            {
                var firstPath = Path.Combine(root.FullName, "First.mkv");
                var secondPath = Path.Combine(root.FullName, "Second.mkv");
                var data = CachedData(root.FullName, firstPath, secondPath);
                data.VideosByPath[firstPath] = data.VideosByPath[firstPath] with
                {
                    Title = "첫째",
                    ReleaseDate = new DateOnly(2020, 1, 1)
                };
                data.VideosByPath[secondPath] = data.VideosByPath[secondPath] with
                {
                    Title = "둘째",
                    ReleaseDate = new DateOnly(2021, 1, 1)
                };
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName),
                    new StubScanner(firstPath, secondPath), data);
                await vm.ScanAsync();
                vm.SelectedSort = VideoSort.ReleaseDate;
                vm.SelectedVideo = vm.Videos.Single(video => video.Path == firstPath);
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.ReleaseDate = new DateTime(2022, 1, 1);

                Assert.IsTrue(await editor.SaveAsync());

                var ordered = vm.VisibleVideos.Cast<VideoItemViewModel>().ToArray();
                Assert.AreEqual(firstPath, ordered[0].Path);
                Assert.AreEqual(secondPath, ordered[1].Path);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task MetadataSave_WhenJsonCommitFails_PreservesOldStateAndDeletesNewPosterCopy()
    {
        var root = Directory.CreateTempSubdirectory("dabom-metadata-rollback-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var posters = Directory.CreateDirectory(Path.Combine(root.FullName, "posters"));
            var oldPoster = Path.Combine(posters.FullName, "old.png");
            var newPoster = Path.Combine(root.FullName, "new.png");
            WritePng(oldPoster);
            WritePng(newPoster);
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Title = "이전 제목",
                Poster = "posters/old.png"
            };
            await new LibraryStore(root.FullName).SaveAsync(data);
            var store = new LibraryStore(
                root.FullName, (_, _, _) => throw new IOException("disk full"));
            var vm = CreateViewModel(store, new StubScanner(path), data);
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single();
            var editor = vm.CreateMetadataEditor();
            Assert.IsNotNull(editor);
            editor.Title = "새 제목";
            editor.ChoosePoster(newPoster);
            var preview = editor.PreviewPoster;

            var saved = await editor.SaveAsync();

            Assert.IsFalse(saved);
            Assert.AreEqual("새 제목", editor.Title);
            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.AreEqual(newPoster, editor.SelectedPosterSourcePath);
            StringAssert.Contains(editor.ErrorMessage, "메타데이터를 저장하지 못했습니다");
            Assert.AreEqual("이전 제목", vm.Videos.Single().Record.Title);
            Assert.AreEqual("posters/old.png", vm.Videos.Single().Record.Poster);
            Assert.IsTrue(File.Exists(oldPoster));
            var reloaded = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.AreEqual("이전 제목", reloaded.VideosByPath[path].Title);
            Assert.AreEqual("posters/old.png", reloaded.VideosByPath[path].Poster);
            Assert.AreEqual(1, Directory.EnumerateFiles(posters.FullName).Count());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void MetadataSave_AddsOnlyActuallyChangedUserEditedFields()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-edited-fields-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var references = new[]
                {
                    new ProviderReference("test", "movie", "1")
                };
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "이전 제목",
                    Synopsis = "같은 줄거리",
                    MetadataStatus = MetadataStatus.Failed,
                    ProviderReferences = references
                };
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName),
                    new StubScanner(path),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.Title = "사용자 제목";
                editor.Synopsis =
                    editor.OriginalRecord.Synopsis ?? string.Empty;

                Assert.IsTrue(await editor.SaveAsync());

                var updated = vm.Videos.Single().Record;
                CollectionAssert.AreEquivalent(
                    new[] { MetadataField.Title },
                    updated.UserEditedFields.ToArray());
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_PreservesStatusAndProviderReferences()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory(
                "dabom-metadata-lifecycle-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var references = new[]
                {
                    new ProviderReference("test", "movie", "1")
                };
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "이전 제목",
                    MetadataStatus = MetadataStatus.Failed,
                    ProviderReferences = references
                };
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName),
                    new StubScanner(path),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.Title = "사용자 제목";

                Assert.IsTrue(await editor.SaveAsync());

                var updated = vm.Videos.Single().Record;
                Assert.AreEqual(
                    MetadataStatus.Failed,
                    updated.MetadataStatus);
                CollectionAssert.AreEqual(
                    references,
                    updated.ProviderReferences);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task CreateMetadataEditor_InjectsManualLookupCallbacks()
    {
        var root = Directory.CreateTempSubdirectory("dabom-manual-callbacks-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            var candidate = new MetadataCandidate(
                "test", "movie", "1", MediaType.Movie);
            var provider = new TestProvider(
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                    [candidate]),
                (_, _) => Task.FromResult(MovieDetails("검색 결과", "1")));
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);
            await vm.ScanAsync();
            vm.SelectedVideo = vm.Videos.Single();
            var editor = vm.CreateMetadataEditor();
            Assert.IsNotNull(editor);
            editor.SearchText = "검색";

            Assert.IsTrue(await editor.SearchAsync());
            Assert.AreEqual("검색", provider.LastQuery!.Title);
            Assert.AreEqual(MediaType.Unknown, provider.LastQuery.MediaType);
            Assert.IsTrue(await editor.SelectCandidateAsync(candidate));
            Assert.AreSame(candidate, provider.LastCandidate);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void MetadataSave_SelectedResultReplacesProviderStateAndTracksOnlyLaterEdits()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-selected-save-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "이전 제목",
                    Genres = ["보호 장르"],
                    Poster = "posters/old.png",
                    MetadataStatus = MetadataStatus.Failed,
                    ProviderReferences = [new("old", "movie", "old")],
                    UserEditedFields =
                    [
                        MetadataField.Title,
                        MetadataField.Genres,
                        MetadataField.Poster
                    ]
                };
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails("선택 제목", "1")));
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.SearchText = "선택";

                Assert.IsTrue(await editor.SearchAsync());
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));
                editor.Synopsis = "사용자 줄거리";

                var saved = await editor.SaveAsync();
                var updated = vm.Videos.Single().Record;
                var reloaded = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);

                Assert.IsTrue(saved);
                Assert.AreEqual(MetadataStatus.Matched, updated.MetadataStatus);
                Assert.AreEqual(MediaType.Movie, updated.MediaType);
                CollectionAssert.AreEqual(new[] { "드라마" }, updated.Genres);
                Assert.AreEqual(
                    "test",
                    updated.ProviderReferences.Single().ProviderKey);
                CollectionAssert.AreEquivalent(
                    new[] { MetadataField.Synopsis },
                    updated.UserEditedFields.ToArray());
                var persisted = reloaded.VideosByPath[path];
                Assert.AreEqual(updated.Title, persisted.Title);
                Assert.AreEqual(updated.MetadataStatus, persisted.MetadataStatus);
                Assert.AreEqual(
                    updated.ProviderReferences.Single(),
                    persisted.ProviderReferences.Single());
                CollectionAssert.AreEquivalent(
                    updated.UserEditedFields.ToArray(),
                    persisted.UserEditedFields.ToArray());
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_SelectedTvEpisodePersistsStructuredValuesAndReferences()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-tv-save-");
            try
            {
                var path = Path.Combine(root.FullName, "Episode.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "기존 제목",
                    MetadataStatus = MetadataStatus.NotFound
                };
                var candidate = new MetadataCandidate(
                    "test",
                    "tv-series",
                    "series-2",
                    MediaType.TvEpisode,
                    DisplayTitle: "시리즈");
                var references = new[]
                {
                    new ProviderReference("test", "tv-series", "series-2"),
                    new ProviderReference("test", "tv-episode", "episode-20")
                };
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (value, _) => Task.FromResult(new MetadataDetails(
                        MediaType: MediaType.TvEpisode,
                        Title: null,
                        OriginalTitle: "Series",
                        SeriesTitle: "시리즈",
                        EpisodeTitle: "회차",
                        ReleaseDate: new DateOnly(2024, 2, 3),
                        Genres: ["드라마"],
                        Director: "감독",
                        Actors: ["배우"],
                        Synopsis: "줄거리",
                        SeasonNumber: value.SeasonNumber,
                        EpisodeNumber: value.EpisodeNumber,
                        PosterUri: null,
                        ProviderReferences: references)));
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.SearchText = "시리즈";

                Assert.IsTrue(await editor.SearchAsync());
                Assert.IsFalse(await editor.SelectCandidateAsync(candidate));
                editor.SeasonNumberText = "2";
                editor.EpisodeNumberText = "3";
                Assert.IsTrue(await editor.ApplyTvEpisodeAsync());
                Assert.IsTrue(await editor.SaveAsync());

                var updated = vm.Videos.Single().Record;
                var persisted = (await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None)).VideosByPath[path];
                foreach (var record in new[] { updated, persisted })
                {
                    Assert.AreEqual(MetadataStatus.Matched, record.MetadataStatus);
                    Assert.AreEqual(MediaType.TvEpisode, record.MediaType);
                    Assert.AreEqual("시리즈 S02E03 · 회차", record.Title);
                    Assert.AreEqual("시리즈", record.SeriesTitle);
                    Assert.AreEqual("회차", record.EpisodeTitle);
                    Assert.AreEqual(2, record.SeasonNumber);
                    Assert.AreEqual(3, record.EpisodeNumber);
                    CollectionAssert.AreEqual(
                        new[] { "드라마" },
                        record.Genres);
                    CollectionAssert.AreEqual(
                        references,
                        record.ProviderReferences);
                    Assert.AreEqual(0, record.UserEditedFields.Count);
                }
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_DownloadsSelectedRemotePosterOnlyWhenSaving()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-remote-poster-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var sourcePoster = Path.Combine(root.FullName, "source.png");
                WritePng(sourcePoster);
                var posterUri = new Uri("https://image.tmdb.org/remote.png");
                var handler = new ResponseHandler(_ => new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(sourcePoster))
                });
                using var imageClient = new HttpClient(handler);
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(
                        MovieDetails("선택 제목", "1", posterUri)));
                var data = CachedData(root.FullName, path);
                var store = new LibraryStore(root.FullName);
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);

                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));
                Assert.AreEqual(0, handler.Calls);
                var postersPath = Path.Combine(root.FullName, "posters");
                Assert.IsFalse(Directory.Exists(postersPath));

                Assert.IsTrue(await editor.SaveAsync());

                Assert.AreEqual(1, handler.Calls);
                var poster = vm.Videos.Single().Record.Poster;
                StringAssert.StartsWith(poster, "posters/");
                Assert.IsTrue(File.Exists(store.ResolvePosterPath(poster)));
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_LocalPosterOverridesSelectedRemotePoster()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-local-priority-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var localPoster = Path.Combine(root.FullName, "local.png");
                WritePng(localPoster);
                var handler = new ResponseHandler(_ => new(HttpStatusCode.OK));
                using var imageClient = new HttpClient(handler);
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails(
                        "선택 제목",
                        "1",
                        new Uri("https://image.tmdb.org/remote.png"))));
                var data = CachedData(root.FullName, path);
                var store = new LibraryStore(root.FullName);
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));

                editor.ChoosePoster(localPoster);
                Assert.IsTrue(await editor.SaveAsync());

                Assert.AreEqual(0, handler.Calls);
                var updated = vm.Videos.Single().Record;
                Assert.IsTrue(File.Exists(store.ResolvePosterPath(updated.Poster)));
                Assert.IsTrue(
                    updated.UserEditedFields.Contains(MetadataField.Poster));
                Assert.AreEqual(MetadataStatus.Matched, updated.MetadataStatus);
                Assert.AreEqual(
                    "test",
                    updated.ProviderReferences.Single().ProviderKey);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_RemovedPosterOverridesSelectedRemotePoster()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-remove-priority-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var posters = Directory.CreateDirectory(
                    Path.Combine(root.FullName, "posters"));
                WritePng(Path.Combine(posters.FullName, "old.png"));
                var handler = new ResponseHandler(_ => new(HttpStatusCode.OK));
                using var imageClient = new HttpClient(handler);
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails(
                        "선택 제목",
                        "1",
                        new Uri("https://image.tmdb.org/remote.png"))));
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Poster = "posters/old.png"
                };
                var store = new LibraryStore(root.FullName);
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));

                editor.MarkPosterRemoved();
                Assert.IsTrue(await editor.SaveAsync());

                Assert.AreEqual(0, handler.Calls);
                var updated = vm.Videos.Single().Record;
                Assert.IsNull(updated.Poster);
                Assert.IsTrue(
                    updated.UserEditedFields.Contains(MetadataField.Poster));
                Assert.AreEqual(MetadataStatus.Matched, updated.MetadataStatus);
                Assert.AreEqual(
                    "test",
                    updated.ProviderReferences.Single().ProviderKey);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_WhenRemotePosterFails_PreservesOldState()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-remote-failure-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var posters = Directory.CreateDirectory(
                    Path.Combine(root.FullName, "posters"));
                var oldPoster = Path.Combine(posters.FullName, "old.png");
                WritePng(oldPoster);
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "이전 제목",
                    Poster = "posters/old.png"
                };
                var normalStore = new LibraryStore(root.FullName);
                await normalStore.SaveAsync(data);
                var handler = new ResponseHandler(_ =>
                    new(HttpStatusCode.ServiceUnavailable));
                using var imageClient = new HttpClient(handler);
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails(
                        "새 제목",
                        "1",
                        new Uri("https://image.tmdb.org/remote.png"))));
                var vm = new MainViewModel(
                    normalStore,
                    new StubScanner(path),
                    CreateEnrichment(normalStore, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));

                Assert.IsFalse(await editor.SaveAsync());
                Assert.AreEqual("이전 제목", vm.Videos.Single().Record.Title);
                Assert.AreEqual(
                    "posters/old.png",
                    vm.Videos.Single().Record.Poster);
                Assert.IsTrue(File.Exists(oldPoster));

                var reloaded = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.AreEqual("이전 제목", reloaded.VideosByPath[path].Title);
                Assert.AreEqual(
                    "posters/old.png",
                    reloaded.VideosByPath[path].Poster);
                Assert.AreEqual(
                    1,
                    Directory.EnumerateFiles(posters.FullName).Count());
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void MetadataSave_WhenRemotePosterJsonCommitFails_RollsBackNewPoster()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-remote-json-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var posters = Directory.CreateDirectory(
                    Path.Combine(root.FullName, "posters"));
                var oldPoster = Path.Combine(posters.FullName, "old.png");
                var sourcePoster = Path.Combine(root.FullName, "source.png");
                WritePng(oldPoster);
                WritePng(sourcePoster);
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "이전 제목",
                    Poster = "posters/old.png"
                };
                await new LibraryStore(root.FullName).SaveAsync(data);
                var store = new LibraryStore(
                    root.FullName,
                    (_, _, _) => throw new IOException("disk full"));
                var handler = new ResponseHandler(_ => new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(sourcePoster))
                });
                using var imageClient = new HttpClient(handler);
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails(
                        "새 제목",
                        "1",
                        new Uri("https://image.tmdb.org/remote.png"))));
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));

                Assert.IsFalse(await editor.SaveAsync());
                Assert.AreEqual("이전 제목", vm.Videos.Single().Record.Title);
                Assert.AreEqual(
                    "posters/old.png",
                    vm.Videos.Single().Record.Poster);
                Assert.IsTrue(File.Exists(oldPoster));

                var reloaded = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.AreEqual("이전 제목", reloaded.VideosByPath[path].Title);
                Assert.AreEqual(
                    "posters/old.png",
                    reloaded.VideosByPath[path].Poster);
                Assert.AreEqual(
                    1,
                    Directory.EnumerateFiles(posters.FullName).Count());
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task InitializeAsync_WithoutLocations_ShowsEmptyStateWithoutScanning()
    {
        var root = Directory.CreateTempSubdirectory("dabom-initialize-empty-");
        try
        {
            var scanner = new StubScanner();
            var vm = CreateViewModel(new LibraryStore(root.FullName), scanner, new LibraryData());

            await vm.InitializeAsync(null);

            Assert.AreEqual(0, scanner.Calls);
            Assert.AreEqual(
                "보관 위치를 추가해 동영상 라이브러리를 시작하세요.", vm.StatusMessage);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_WithLocation_ScansOnce()
    {
        var root = Directory.CreateTempSubdirectory("dabom-initialize-scan-");
        try
        {
            var scanner = new StubScanner();
            var vm = CreateViewModel(
                new LibraryStore(root.FullName), scanner,
                new LibraryData { Locations = [root.FullName] });

            await vm.InitializeAsync("시작 경고");

            Assert.AreEqual(1, scanner.Calls);
            Assert.AreEqual("폴더 확인을 마쳤습니다.", vm.StatusMessage);
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

    private static MetadataEnrichmentService CreateEnrichment(
        LibraryStore store,
        HttpClient imageClient,
        IMetadataProvider provider) =>
        new(new MediaFilenameParser(), [provider], store, imageClient);

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

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static MetadataDetails MovieDetails(
        string title,
        string id,
        Uri? posterUri = null) => new(
        MediaType: MediaType.Movie,
        Title: title,
        OriginalTitle: title,
        SeriesTitle: null,
        EpisodeTitle: null,
        ReleaseDate: new DateOnly(2024, 1, 1),
        Genres: ["드라마"],
        Director: "감독",
        Actors: ["배우"],
        Synopsis: "줄거리",
        SeasonNumber: null,
        EpisodeNumber: null,
        PosterUri: posterUri,
        ProviderReferences: [new("test", "movie", id)]);

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

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class TestProvider(
        Func<MetadataQuery, CancellationToken,
            Task<IReadOnlyList<MetadataCandidate>>> search,
        Func<MetadataCandidate, CancellationToken, Task<MetadataDetails>> details)
        : IMetadataProvider
    {
        public string ProviderKey => "test";
        public MetadataQuery? LastQuery { get; private set; }
        public MetadataCandidate? LastCandidate { get; private set; }

        public Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
            MetadataQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return search(query, cancellationToken);
        }

        public Task<MetadataDetails> GetDetailsAsync(
            MetadataCandidate candidate,
            CancellationToken cancellationToken)
        {
            LastCandidate = candidate;
            return details(candidate, cancellationToken);
        }

        internal static TestProvider ForMovie(MetadataDetails details) =>
            new(
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                [
                    new("test", "movie", "1", MediaType.Movie)
                ]),
                (_, _) => Task.FromResult(details));
    }
}
