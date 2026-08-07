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
    public async Task Overview_GroupsBeforeSearchAndKeepsActualVideoCounts()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-overview-");
        try
        {
            var first = Path.Combine(root.FullName, "Alpha.mkv");
            var second = Path.Combine(root.FullName, "Beta.mkv");
            var movie = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, first, second, movie);
            data.VideosByPath[first] = TvRecord("Alpha", "시리즈", 1, 1);
            data.VideosByPath[second] = TvRecord("Beta", "시리즈", 1, 1);
            data.VideosByPath[movie] = data.VideosByPath[movie] with
            {
                Title = "Movie",
                MediaType = MediaType.Movie
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(first, second, movie),
                data);
            await vm.ScanAsync();

            Assert.AreEqual(3, vm.VisibleCount);
            Assert.AreEqual(2, vm.DisplayItemCount);
            Assert.AreEqual(1, vm.VisibleItems.OfType<SeasonItemViewModel>().Count());

            vm.SearchText = "Alpha";

            var season = vm.VisibleItems.OfType<SeasonItemViewModel>().Single();
            Assert.AreEqual(1, vm.VisibleCount);
            Assert.AreEqual(1, vm.DisplayItemCount);
            Assert.AreEqual(1, season.EpisodeCount);
            Assert.AreEqual(1, Filter(vm, LibraryFilterKind.All).Count);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task Overview_SeparatesProviderKeysAndUsesFirstSortedEpisodePosition()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-order-");
        try
        {
            var a = Path.Combine(root.FullName, "A.mkv");
            var b = Path.Combine(root.FullName, "B.mkv");
            var c = Path.Combine(root.FullName, "C.mkv");
            var d = Path.Combine(root.FullName, "D.mkv");
            var data = CachedData(root.FullName, a, b, c, d);
            data.VideosByPath[a] = TvRecord("Bravo", "이름 A", 1, 2, "10") with
            {
                ReleaseDate = new DateOnly(2020, 1, 1)
            };
            data.VideosByPath[b] = TvRecord("Alpha", "이름 B", 1, 1, "10") with
            {
                ReleaseDate = new DateOnly(2024, 1, 1)
            };
            data.VideosByPath[c] = TvRecord("Charlie", "이름 A", 1, 3, "11");
            data.VideosByPath[d] = TvRecord("Delta", "이름 A", 1, 4);
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(a, b, c, d),
                data);
            await vm.ScanAsync();

            var titleSeason = vm.VisibleItems.OfType<SeasonItemViewModel>().Single();
            Assert.AreEqual("이름 B", titleSeason.DisplayTitle);
            CollectionAssert.AreEqual(
                new[] { "Alpha", "Bravo" },
                titleSeason.Episodes.Select(video => video.DisplayTitle).ToArray());
            Assert.AreEqual(2, vm.VisibleItems.OfType<VideoItemViewModel>().Count());

            vm.SelectedSort = VideoSort.ReleaseDate;

            var releaseSeason = vm.VisibleItems.OfType<SeasonItemViewModel>().Single();
            Assert.AreEqual("이름 B", releaseSeason.DisplayTitle);
            CollectionAssert.AreEqual(
                new[] { b, a },
                releaseSeason.Episodes.Select(video => video.Path).ToArray());
            Assert.AreSame(releaseSeason, vm.VisibleItems[0]);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task Overview_FileModifiedSortOrdersGroupAndItsEpisodesTogether()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-file-order-");
        try
        {
            var first = Path.Combine(root.FullName, "First.mkv");
            var second = Path.Combine(root.FullName, "Second.mkv");
            var movie = Path.Combine(root.FullName, "Movie.mkv");
            var firstTime = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var secondTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var movieTime = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var data = CachedData(root.FullName, first, second, movie);
            data.VideosByPath[first] = TvRecord("First", "시리즈", 1, 1, "10") with
            {
                LastWriteTimeUtc = firstTime
            };
            data.VideosByPath[second] = TvRecord("Second", "시리즈", 1, 2, "10") with
            {
                LastWriteTimeUtc = secondTime
            };
            data.VideosByPath[movie] = data.VideosByPath[movie] with
            {
                Title = "Movie",
                MediaType = MediaType.Movie,
                LastWriteTimeUtc = movieTime
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new TimestampScanner(
                    (first, firstTime),
                    (second, secondTime),
                    (movie, movieTime)),
                data);
            await vm.ScanAsync();

            vm.SelectedSort = VideoSort.FileModified;

            var season = (SeasonItemViewModel)vm.VisibleItems[0];
            Assert.AreEqual(movie, ((VideoItemViewModel)vm.VisibleItems[1]).Path);
            CollectionAssert.AreEqual(
                new[] { second, first },
                season.Episodes.Select(video => video.Path).ToArray());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SeasonView_KeepsConditionsAndHeadingAcrossEmptyResultsAndClearsSelection()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-view-");
        try
        {
            var first = Path.Combine(root.FullName, "Alpha.mkv");
            var second = Path.Combine(root.FullName, "Beta.mkv");
            var data = CachedData(root.FullName, first, second);
            data.VideosByPath[first] = TvRecord("Alpha", "표시명 A", 3, 1, "10");
            data.VideosByPath[second] = TvRecord("Beta", "표시명 B", 3, 2, "10");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(first, second),
                data);
            await vm.ScanAsync();
            var featured = vm.FeaturedVideo;
            var season = vm.VisibleItems.OfType<SeasonItemViewModel>().Single();
            vm.SelectedItem = season;

            Assert.IsNull(vm.SelectedVideo);
            Assert.IsTrue(vm.OpenSeason(season));
            Assert.IsTrue(vm.IsSeasonView);
            Assert.AreEqual("표시명 A · 시즌 3", vm.SeasonHeading);
            Assert.IsNull(vm.SelectedItem);
            CollectionAssert.AreEqual(
                new[] { first, second },
                vm.VisibleItems.Cast<VideoItemViewModel>()
                    .Select(video => video.Path)
                    .ToArray());

            vm.SelectedItem = vm.VisibleItems.Cast<VideoItemViewModel>().First();
            vm.SearchText = "일치하지 않음";

            Assert.AreEqual(0, vm.DisplayItemCount);
            Assert.IsTrue(vm.IsSeasonView);
            Assert.AreEqual("표시명 A · 시즌 3", vm.SeasonHeading);
            Assert.IsTrue(vm.IsFilterEmptyStateVisible);
            Assert.IsNull(vm.SelectedVideo);

            vm.SearchText = "Beta";
            Assert.AreEqual("표시명 B · 시즌 3", vm.SeasonHeading);
            Assert.AreEqual(1, vm.DisplayItemCount);
            vm.CloseSeason();

            Assert.IsFalse(vm.IsSeasonView);
            Assert.IsNull(vm.SelectedItem);
            Assert.AreEqual("Beta", vm.SearchText);
            Assert.AreSame(featured, vm.FeaturedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SeasonView_WhenCurrentGroupDropsBelowTwo_ReturnsToOverview()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-collapse-");
        try
        {
            var first = Path.Combine(root.FullName, "Alpha.mkv");
            var second = Path.Combine(root.FullName, "Beta.mkv");
            var data = CachedData(root.FullName, first, second);
            data.VideosByPath[first] = TvRecord("Alpha", "시리즈", 1, 1, "10");
            data.VideosByPath[second] = TvRecord("Beta", "시리즈", 1, 2, "10");
            var scanner = new SequenceScanner(Scan(first, second), Scan(first));
            var vm = CreateViewModel(new LibraryStore(root.FullName), scanner, data);
            await vm.ScanAsync();
            Assert.IsTrue(vm.OpenSeason(
                vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));
            vm.SelectedVideo = vm.Videos.Single(video => video.Path == first);

            await vm.ScanAsync();

            Assert.IsFalse(vm.IsSeasonView);
            Assert.IsNull(vm.SelectedItem);
            Assert.AreEqual(1, vm.VisibleCount);
            Assert.AreEqual(first, vm.VisibleItems.Cast<VideoItemViewModel>().Single().Path);
            Assert.IsNull(vm.Videos.Single().Record.LastPlayedUtc);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task FilterOptions_NormalizeSortDeduplicateAndCountAgainstSearch()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-options-");
        try
        {
            var alpha = Path.Combine(root.FullName, "Alpha.mkv");
            var beta = Path.Combine(root.FullName, "Beta.mkv");
            var gamma = Path.Combine(root.FullName, "Gamma.mkv");
            var data = CachedData(root.FullName, alpha, beta, gamma);
            data.VideosByPath[alpha] = data.VideosByPath[alpha] with
            {
                Title = "Alpha",
                Genres = [" Drama ", "가족", "  "],
                MetadataStatus = MetadataStatus.Pending
            };
            data.VideosByPath[beta] = data.VideosByPath[beta] with
            {
                Title = "Beta",
                Genres = ["drama", "코미디"],
                MetadataStatus = MetadataStatus.Matched
            };
            data.VideosByPath[gamma] = data.VideosByPath[gamma] with
            {
                Title = "Gamma",
                Genres = ["드라마 코미디"],
                MetadataStatus = MetadataStatus.Failed
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(alpha, beta, gamma),
                data);

            await vm.ScanAsync();

            Assert.AreEqual("전체 영상", vm.SelectedFilter!.ToString());
            Assert.AreEqual("영상 필터: 전체 영상", vm.FilterAutomationName);
            var genres = vm.FilterOptions
                .Where(option => option.Kind == LibraryFilterKind.Genre)
                .ToArray();
            Assert.AreEqual(4, genres.Length);
            Assert.AreEqual(1, genres.Count(option =>
                option.Genre!.Equals("Drama", StringComparison.CurrentCultureIgnoreCase)));
            Assert.IsFalse(genres.Any(option => string.IsNullOrWhiteSpace(option.Genre)));
            CollectionAssert.AreEqual(
                genres.Select(option => option.Label)
                    .OrderBy(label => label, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                genres.Select(option => option.Label).ToArray());

            vm.SearchText = "Alpha";

            Assert.AreEqual(1, Filter(vm, LibraryFilterKind.All).Count);
            Assert.AreEqual(1, Filter(vm, LibraryFilterKind.MissingMetadata).Count);
            Assert.AreEqual(1, Filter(vm, LibraryFilterKind.Genre, "Drama").Count);
            Assert.AreEqual(0, Filter(vm, LibraryFilterKind.Genre, "코미디").Count);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task GenreFilter_UsesCaseInsensitiveNormalizedExactMatchAndSearchAnd()
    {
        var root = Directory.CreateTempSubdirectory("dabom-genre-filter-");
        try
        {
            var alpha = Path.Combine(root.FullName, "Alpha.mkv");
            var beta = Path.Combine(root.FullName, "Beta.mkv");
            var gamma = Path.Combine(root.FullName, "Gamma.mkv");
            var data = CachedData(root.FullName, alpha, beta, gamma);
            data.VideosByPath[alpha] = data.VideosByPath[alpha] with
            {
                Title = "Alpha",
                Genres = [" Drama "]
            };
            data.VideosByPath[beta] = data.VideosByPath[beta] with
            {
                Title = "Beta",
                Genres = ["drama"]
            };
            data.VideosByPath[gamma] = data.VideosByPath[gamma] with
            {
                Title = "Gamma",
                Genres = ["Drama Comedy"]
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(alpha, beta, gamma),
                data);
            await vm.ScanAsync();
            var featured = vm.FeaturedVideo;

            vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "DRAMA");

            CollectionAssert.AreEquivalent(
                new[] { alpha, beta },
                vm.VisibleVideos.Cast<VideoItemViewModel>()
                    .Select(video => video.Path)
                    .ToArray());
            Assert.AreSame(featured, vm.FeaturedVideo);

            vm.SelectedVideo = vm.Videos.Single(video => video.Path == alpha);
            vm.SearchText = "Alpha";
            Assert.AreEqual(1, vm.VisibleCount);
            Assert.IsNotNull(vm.SelectedVideo);

            vm.SearchText = "Gamma";
            Assert.AreEqual(0, vm.VisibleCount);
            Assert.IsNull(vm.SelectedVideo);

            vm.SelectedSort = VideoSort.ReleaseDate;
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.All);

            Assert.AreEqual("Gamma", vm.SearchText);
            Assert.AreEqual(VideoSort.ReleaseDate, vm.SelectedSort);
            Assert.AreEqual(1, vm.VisibleCount);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [DataTestMethod]
    [DataRow(MetadataStatus.Unspecified, true)]
    [DataRow(MetadataStatus.Pending, true)]
    [DataRow(MetadataStatus.NotFound, true)]
    [DataRow(MetadataStatus.Failed, true)]
    [DataRow(MetadataStatus.Matched, false)]
    [DataRow(MetadataStatus.Manual, false)]
    public async Task MissingMetadataFilter_UsesOnlyApprovedStatuses(
        MetadataStatus status,
        bool expectedVisible)
    {
        var root = Directory.CreateTempSubdirectory("dabom-status-filter-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                MetadataStatus = status
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(path),
                data);
            await vm.ScanAsync();

            vm.SelectedFilter = Filter(vm, LibraryFilterKind.MissingMetadata);

            Assert.AreEqual(expectedVisible ? 1 : 0, vm.VisibleCount);
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
    public void VideoItem_ExposesActualFileNameWithExtension()
    {
        var root = Directory.CreateTempSubdirectory("dabom-file-name-");
        try
        {
            var item = new VideoItemViewModel(
                @"D:\Movies\Movie.2024.mkv",
                new VideoRecord(),
                new LibraryStore(root.FullName));

            Assert.AreEqual("Movie.2024.mkv", item.FileName);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void SeasonGroupKey_UsesEligibilityProviderPriorityAndNormalizedTitleFallback()
    {
        var providerA = new VideoRecord
        {
            MediaType = MediaType.TvEpisode,
            SeriesTitle = "표시명 A",
            SeasonNumber = 2,
            ProviderReferences = [new("tmdb", "tv-series", "10")]
        };
        var providerRenamed = providerA with { SeriesTitle = "표시명 B" };
        var providerB = providerA with
        {
            ProviderReferences = [new("tmdb", "tv-series", "11")]
        };
        var titleA = providerA with
        {
            SeriesTitle = "  ＤＡＢＯＭ  ",
            ProviderReferences = []
        };
        var titleB = titleA with { SeriesTitle = "dabom" };

        Assert.AreEqual(
            SeasonGroupKey.From(providerA),
            SeasonGroupKey.From(providerRenamed));
        Assert.AreNotEqual(
            SeasonGroupKey.From(providerA),
            SeasonGroupKey.From(providerB));
        Assert.AreNotEqual(
            SeasonGroupKey.From(providerA),
            SeasonGroupKey.From(providerA with
            {
                ProviderReferences = [new("other", "tv-series", "10")]
            }));
        Assert.AreNotEqual(
            SeasonGroupKey.From(providerA),
            SeasonGroupKey.From(titleA));
        Assert.AreEqual(
            SeasonGroupKey.From(titleA),
            SeasonGroupKey.From(titleB));
        Assert.IsNotNull(SeasonGroupKey.From(titleA with { EpisodeNumber = null }));
        Assert.IsNull(SeasonGroupKey.From(titleA with { MediaType = MediaType.Movie }));
        Assert.IsNull(SeasonGroupKey.From(titleA with { SeriesTitle = "  " }));
        Assert.IsNull(SeasonGroupKey.From(titleA with { SeasonNumber = 0 }));
    }

    [TestMethod]
    public void SeasonItem_UsesMatchedOrderForTextAndWholeGroupForPoster()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-item-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "posters"));
            WritePng(Path.Combine(root.FullName, "posters", "season.png"));
            var store = new LibraryStore(root.FullName);
            var first = new VideoItemViewModel(
                @"D:\Series\Second.mkv",
                new VideoRecord
                {
                    MediaType = MediaType.TvEpisode,
                    SeriesTitle = "첫 표시명",
                    SeasonNumber = 4
                },
                store);
            var hiddenPoster = new VideoItemViewModel(
                @"D:\Series\Hidden.mkv",
                first.Record with
                {
                    SeriesTitle = "숨은 표시명",
                    Poster = "posters/season.png"
                },
                store);
            var second = new VideoItemViewModel(
                @"D:\Series\Third.mkv",
                first.Record with { SeriesTitle = "다른 표시명" },
                store);
            var key = SeasonGroupKey.From(first.Record)!;

            var season = new SeasonItemViewModel(
                key,
                [first, second],
                [first, hiddenPoster, second]);

            Assert.AreEqual("첫 표시명", season.DisplayTitle);
            Assert.AreEqual(4, season.SeasonNumber);
            Assert.AreEqual(2, season.EpisodeCount);
            Assert.AreEqual("시즌 4 · 2편", season.Summary);
            CollectionAssert.AreEqual(
                new[] { first, second },
                season.Episodes.ToArray());
            Assert.AreSame(hiddenPoster.Poster, season.Poster);
            Assert.IsTrue(season.HasPoster);
            Assert.AreEqual(
                "첫 표시명, 시즌 4, 2편, 시즌 열기",
                season.AutomationName);
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
    public async Task ScanAsync_WhenEnrichedSelectedVideoLeavesMetadataFilter_RefreshesViewAndSelection()
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
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.MissingMetadata);
            vm.SelectedVideo = vm.Videos.Single();
            Assert.IsTrue(vm.IsScanning);
            Assert.AreEqual(1, vm.VisibleCount);
            release.TrySetResult();
            await scan;

            Assert.AreEqual(0, vm.VisibleCount);
            Assert.IsNull(vm.SelectedVideo);
            Assert.AreEqual(
                0,
                Filter(vm, LibraryFilterKind.MissingMetadata).Count);
            Assert.AreEqual(LibraryFilterKind.MissingMetadata, vm.SelectedFilter!.Kind);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenActiveGenreDisappears_KeepsZeroOptionUntilSelectionChanges()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-vanished-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Genres = ["액션"],
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
                    return MovieDetails("Movie", "1");
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
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "액션");
            vm.SelectedVideo = vm.Videos.Single();
            release.TrySetResult();
            await scan;

            Assert.AreEqual(0, vm.VisibleCount);
            Assert.IsNull(vm.SelectedVideo);
            Assert.AreEqual(
                0,
                Filter(vm, LibraryFilterKind.Genre, "액션").Count);
            Assert.AreEqual("액션", vm.SelectedFilter!.Genre);
            Assert.AreEqual(
                1,
                Filter(vm, LibraryFilterKind.Genre, "드라마").Count);

            vm.SelectedFilter = Filter(vm, LibraryFilterKind.All);

            Assert.IsFalse(vm.FilterOptions.Any(option =>
                string.Equals(
                    option.Genre,
                    "액션",
                    StringComparison.CurrentCultureIgnoreCase)));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenEnrichedVideoStillMatchesGenreFilter_KeepsSelection()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-stays-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Genres = ["드라마"],
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
                    return MovieDetails("Movie", "1");
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
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "드라마");
            var selected = vm.Videos.Single();
            vm.SelectedVideo = selected;
            release.TrySetResult();
            await scan;

            Assert.AreSame(selected, vm.SelectedVideo);
            Assert.AreEqual(1, vm.VisibleCount);
            Assert.AreEqual(
                1,
                Filter(vm, LibraryFilterKind.Genre, "드라마").Count);
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
    public void MetadataSave_WhenSelectedResultLeavesGenreFilter_RefreshesViewAndSelection()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-manual-filter-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    Title = "Movie",
                    Genres = ["액션"],
                    MetadataStatus = MetadataStatus.Manual
                };
                var candidate = new MetadataCandidate(
                    "test", "movie", "1", MediaType.Movie);
                var provider = new TestProvider(
                    (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [candidate]),
                    (_, _) => Task.FromResult(MovieDetails("Movie", "1")));
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                await vm.ScanAsync();
                vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "액션");
                vm.SelectedVideo = vm.Videos.Single();
                var editor = vm.CreateMetadataEditor();
                Assert.IsNotNull(editor);
                editor.SearchText = "Movie";

                Assert.IsTrue(await editor.SearchAsync());
                Assert.IsTrue(await editor.SelectCandidateAsync(candidate));
                Assert.IsTrue(await editor.SaveAsync());

                Assert.AreEqual(0, vm.VisibleCount);
                Assert.IsNull(vm.SelectedVideo);
                Assert.AreEqual(
                    0,
                    Filter(vm, LibraryFilterKind.Genre, "액션").Count);
                Assert.AreEqual(
                    1,
                    Filter(vm, LibraryFilterKind.Genre, "드라마").Count);
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

    [TestMethod]
    public async Task FilterEmptyState_DistinguishesMetadataCompleteFromSearchCombination()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-empty-");
        try
        {
            var matchedPath = Path.Combine(root.FullName, "Matched.mkv");
            var data = CachedData(root.FullName, matchedPath);
            data.VideosByPath[matchedPath] = data.VideosByPath[matchedPath] with
            {
                MetadataStatus = MetadataStatus.Matched
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(matchedPath),
                data);
            await vm.ScanAsync();

            vm.SelectedFilter = Filter(vm, LibraryFilterKind.MissingMetadata);

            Assert.IsTrue(vm.IsFilterEmptyStateVisible);
            Assert.IsTrue(vm.IsMetadataCompleteFilterEmpty);
            Assert.AreEqual(
                "모든 영상의 메타데이터가 준비되었습니다.",
                vm.FilterEmptyTitle);
            Assert.AreEqual(
                "다른 영상을 보려면 ‘전체 영상’을 선택하세요.",
                vm.FilterEmptyGuidance);

            data.VideosByPath[matchedPath] = data.VideosByPath[matchedPath] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            var pendingVm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(matchedPath),
                data);
            await pendingVm.ScanAsync();
            pendingVm.SelectedFilter = Filter(
                pendingVm,
                LibraryFilterKind.MissingMetadata);
            pendingVm.SearchText = "일치하지 않는 검색어";

            Assert.IsTrue(pendingVm.IsFilterEmptyStateVisible);
            Assert.IsFalse(pendingVm.IsMetadataCompleteFilterEmpty);
            Assert.AreEqual(
                "현재 검색과 필터에 맞는 영상이 없습니다.",
                pendingVm.FilterEmptyTitle);
            Assert.AreEqual(
                "검색어를 지우거나 ‘전체 영상’을 선택하세요.",
                pendingVm.FilterEmptyGuidance);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task FilterEmptyState_DoesNotReplaceNoLocationOrEmptyLibraryStates()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-priority-");
        try
        {
            var withoutLocation = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(),
                new LibraryData());
            withoutLocation.SelectedFilter = Filter(
                withoutLocation,
                LibraryFilterKind.MissingMetadata);
            Assert.IsFalse(withoutLocation.IsFilterEmptyStateVisible);

            var emptyLibrary = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(),
                new LibraryData { Locations = [root.FullName] });
            await emptyLibrary.ScanAsync();
            emptyLibrary.SelectedFilter = Filter(
                emptyLibrary,
                LibraryFilterKind.MissingMetadata);
            Assert.IsFalse(emptyLibrary.IsFilterEmptyStateVisible);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task FilterSelection_SurvivesEmptyScanAndReappliesWhenVideoReturns()
    {
        var root = Directory.CreateTempSubdirectory("dabom-filter-rescan-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                Genres = ["드라마"]
            };
            var scanner = new SequenceScanner(Scan(path), Scan(), Scan(path));
            var vm = CreateViewModel(new LibraryStore(root.FullName), scanner, data);
            await vm.ScanAsync();
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "드라마");

            await vm.ScanAsync();

            Assert.AreEqual("드라마", vm.SelectedFilter!.Genre);
            Assert.AreEqual(0, vm.Videos.Count);
            Assert.IsFalse(vm.IsFilterEmptyStateVisible);

            await vm.ScanAsync();

            Assert.AreEqual("드라마", vm.SelectedFilter!.Genre);
            Assert.AreEqual(1, vm.VisibleCount);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static LibraryFilterOption Filter(
        MainViewModel viewModel,
        LibraryFilterKind kind,
        string? genre = null) =>
        viewModel.FilterOptions.Single(option =>
            option.Kind == kind
            && (genre is null || string.Equals(
                option.Genre,
                genre,
                StringComparison.CurrentCultureIgnoreCase)));

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

    private static VideoRecord TvRecord(
        string title,
        string seriesTitle,
        int seasonNumber,
        int? episodeNumber,
        string? seriesId = null) => new()
    {
        Title = title,
        MediaType = MediaType.TvEpisode,
        SeriesTitle = seriesTitle,
        SeasonNumber = seasonNumber,
        EpisodeNumber = episodeNumber,
        FileSizeBytes = 1,
        LastWriteTimeUtc = DateTimeOffset.UnixEpoch,
        ProviderReferences = seriesId is null
            ? []
            : [new("tmdb", "tv-series", seriesId)]
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

    private static ScanResult Scan(params string[] paths)
    {
        var videos = paths.ToDictionary(
            Path.GetFullPath,
            path => new ScannedVideo(
                Path.GetFullPath(path),
                1,
                DateTimeOffset.UnixEpoch,
                null),
            StringComparer.OrdinalIgnoreCase);
        return new(videos, []);
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

    private sealed class TimestampScanner(
        params (string Path, DateTimeOffset LastWriteTimeUtc)[] entries)
        : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken)
        {
            var videos = entries.ToDictionary(
                entry => Path.GetFullPath(entry.Path),
                entry => new ScannedVideo(
                    Path.GetFullPath(entry.Path),
                    1,
                    entry.LastWriteTimeUtc,
                    null),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new ScanResult(videos, []));
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
