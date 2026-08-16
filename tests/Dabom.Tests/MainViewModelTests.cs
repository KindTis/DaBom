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
    public async Task RevealVideo_ClearsConditionsOpensSeasonAndSelectsTarget()
    {
        var root = Directory.CreateTempSubdirectory("dabom-reveal-video-");
        try
        {
            var first = Path.Combine(root.FullName, "Episode.One.mkv");
            var second = Path.Combine(root.FullName, "Episode.Two.mkv");
            var movie = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, first, second, movie);
            data.VideosByPath[first] = TvRecord("Episode One", "Series", 1, 1) with
            {
                Genres = ["Drama"]
            };
            data.VideosByPath[second] = TvRecord("Episode Two", "Series", 1, 2) with
            {
                Genres = ["Drama"]
            };
            data.VideosByPath[movie] = data.VideosByPath[movie] with
            {
                Title = "Movie",
                MediaType = MediaType.Movie,
                Genres = ["Action"]
            };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(first, second, movie),
                data);
            await vm.ScanAsync();
            var target = vm.Videos.Single(video => video.Path == first);
            vm.SearchText = "Movie";
            vm.SelectedFilter = Filter(vm, LibraryFilterKind.Genre, "Action");
            Assert.IsFalse(vm.VisibleItems.Contains(target));

            var revealed = vm.RevealVideo(target);

            Assert.IsTrue(revealed);
            Assert.AreEqual(string.Empty, vm.SearchText);
            Assert.AreEqual(LibraryFilterKind.All, vm.SelectedFilter!.Kind);
            Assert.IsTrue(vm.IsSeasonView);
            Assert.IsTrue(vm.VisibleItems.Contains(target));
            Assert.AreSame(target, vm.SelectedVideo);
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

            vm.IsSortDescending = true;
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

            vm.IsSortDescending = true;
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
    public async Task SeasonView_UsesWholeGroupHeroAndContextAcrossSearch()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-hero-");
        try
        {
            var paths = Enumerable.Range(1, 4)
                .Select(number => Path.Combine(root.FullName, $"Episode {number}.mkv"))
                .ToArray();
            var playedAt = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
            var data = CachedData(root.FullName, paths);
            for (var index = 0; index < paths.Length; index++)
            {
                data.VideosByPath[paths[index]] = TvRecord(
                    $"Episode {index + 1}", "시리즈", 1, index + 1, "10") with
                {
                    EpisodeTitle = $"에피소드 {index + 1}",
                    LastPlayedUtc = index < 2 ? playedAt : null
                };
            }
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(paths),
                data);
            await vm.ScanAsync();
            var featured = vm.FeaturedVideo;

            Assert.IsTrue(vm.OpenSeason(
                vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));

            Assert.AreEqual(paths[2], vm.HeroVideo!.Path);
            Assert.AreEqual(4, vm.ActiveSeason!.TotalEpisodeCount);
            Assert.AreEqual("에피소드", vm.ToolbarContextLabel);
            Assert.AreEqual(4, vm.ToolbarItemCount);

            vm.SearchText = "Episode 4";

            Assert.AreEqual(1, vm.DisplayItemCount);
            Assert.AreEqual(1, vm.ToolbarItemCount);
            Assert.AreEqual(paths[2], vm.HeroVideo!.Path);

            vm.CloseSeason();

            Assert.AreSame(featured, vm.HeroVideo);
            Assert.AreEqual("내 영상", vm.ToolbarContextLabel);
            Assert.AreEqual(vm.VisibleCount, vm.ToolbarItemCount);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SeasonHeroPlayback_AdvancesAndRestartsAfterSavedHistory()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-hero-play-");
        try
        {
            var paths = Enumerable.Range(1, 4)
                .Select(number => Path.Combine(root.FullName, $"Episode {number}.mkv"))
                .ToArray();
            var playedAt = DateTimeOffset.Parse("2026-08-07T00:00:00Z");
            var data = CachedData(root.FullName, paths);
            for (var index = 0; index < paths.Length; index++)
            {
                data.VideosByPath[paths[index]] = TvRecord(
                    $"Episode {index + 1}", "시리즈", 1, index + 1, "10") with
                {
                    LastPlayedUtc = index < 2 ? playedAt : null
                };
            }
            var vm = new MainViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(paths),
                data,
                _ => true,
                () => DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
                _ => 0);
            await vm.ScanAsync();
            var featured = vm.FeaturedVideo;
            Assert.IsTrue(vm.OpenSeason(
                vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));

            await vm.PlayAsync(vm.HeroVideo!);
            Assert.AreEqual(paths[3], vm.HeroVideo!.Path);

            await vm.PlayAsync(vm.HeroVideo!);
            Assert.AreEqual(paths[0], vm.HeroVideo!.Path);
            Assert.AreEqual("처음부터 보기", vm.ActiveSeason!.IntroLabel);
            Assert.AreSame(featured, vm.FeaturedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SeasonHeroPlayback_WhenHistorySaveFails_KeepsCurrentIntro()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-hero-save-");
        try
        {
            var first = Path.Combine(root.FullName, "Episode 1.mkv");
            var second = Path.Combine(root.FullName, "Episode 2.mkv");
            var data = CachedData(root.FullName, first, second);
            data.VideosByPath[first] = TvRecord("Episode 1", "시리즈", 1, 1, "10");
            data.VideosByPath[second] = TvRecord("Episode 2", "시리즈", 1, 2, "10");
            var store = new LibraryStore(
                root.FullName,
                (_, _, _) => throw new IOException("disk full"));
            var vm = new MainViewModel(
                store,
                new StubScanner(first, second),
                data,
                _ => true,
                () => DateTimeOffset.Parse("2026-08-08T00:00:00Z"),
                _ => 0);
            await vm.ScanAsync();
            Assert.IsTrue(vm.OpenSeason(
                vm.VisibleItems.OfType<SeasonItemViewModel>().Single()));
            var intro = vm.HeroVideo;

            await vm.PlayAsync(intro!);

            Assert.AreSame(intro, vm.HeroVideo);
            Assert.IsNull(intro!.Record.LastPlayedUtc);
            StringAssert.Contains(vm.StatusMessage, "재생 이력 저장 실패");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SeasonView_WhenEpisodeBecomesMissing_RemainsInSeason()
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

            Assert.IsTrue(vm.IsSeasonView);
            Assert.AreEqual(2, vm.VisibleCount);
            Assert.AreEqual(2, vm.DisplayItemCount);
            Assert.AreEqual(
                VideoFileStatus.Missing,
                vm.Videos.Single(video => video.Path == second).FileStatus);
            Assert.IsTrue(vm.ActiveSeason!.ContainsMissingFiles);
            Assert.AreEqual(first, vm.SelectedVideo!.Path);
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
    public async Task SeasonItem_UsesMatchedOrderForTextAndWholeGroupForPoster()
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
            await hiddenPoster.LoadPosterAsync();
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
    public void SeasonItem_SelectsFirstUnplayedEpisodeAndFallsBackToFirstOverall()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-intro-");
        try
        {
            var store = new LibraryStore(root.FullName);
            var playedAt = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
            VideoItemViewModel Episode(string name, int? number, bool played) => new(
                Path.Combine(root.FullName, $"{name}.mkv"),
                TvRecord(name, "시리즈", 1, number, "10") with
                {
                    EpisodeTitle = name,
                    LastPlayedUtc = played ? playedAt : null
                },
                store);

            var unknown = Episode("회차 없음", null, false);
            var first = Episode("첫 화", 1, true);
            var second = Episode("두 번째 화", 2, true);
            var third = Episode("세 번째 화", 3, false);
            var fourth = Episode("네 번째 화", 4, false);
            var key = SeasonGroupKey.From(first.Record)!;
            var season = new SeasonItemViewModel(
                key,
                [fourth],
                [unknown, first, second, third, fourth]);

            Assert.AreEqual(5, season.TotalEpisodeCount);
            Assert.AreEqual("시즌 1 · 총 5편", season.TotalSummary);
            Assert.AreSame(third, season.IntroEpisode);
            Assert.AreEqual("다음 미시청 에피소드", season.IntroLabel);
            Assert.AreEqual("3화 · 세 번째 화", season.IntroHeading);

            var allPlayed = new[]
            {
                Episode("첫 화", 1, true),
                Episode("두 번째 화", 2, true),
                Episode("회차 없음", null, true)
            };
            var replay = new SeasonItemViewModel(key, allPlayed, allPlayed);

            Assert.AreSame(allPlayed[0], replay.IntroEpisode);
            Assert.AreEqual("처음부터 보기", replay.IntroLabel);
            Assert.AreEqual("1화 · 첫 화", replay.IntroHeading);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void SeasonItem_WhenEpisodeMissing_DimsPosterAndExtendsAutomationName()
    {
        var root = Directory.CreateTempSubdirectory("dabom-season-missing-");
        try
        {
            var store = new LibraryStore(root.FullName);
            var missing = new VideoItemViewModel(
                Path.Combine(root.FullName, "Missing.mkv"),
                TvRecord("첫 화", "시리즈", 1, 1, "10"),
                store)
            {
                FileStatus = VideoFileStatus.Missing
            };
            var present = new VideoItemViewModel(
                Path.Combine(root.FullName, "Present.mkv"),
                TvRecord("두 번째 화", "시리즈", 1, 2, "10"),
                store);

            var season = new SeasonItemViewModel(
                SeasonGroupKey.From(missing.Record)!,
                [missing, present],
                [missing, present]);

            Assert.IsTrue(season.ContainsMissingFiles);
            Assert.AreEqual(0.5, season.PosterOpacity);
            Assert.AreEqual(
                "시리즈, 시즌 1, 2편, 파일 없음 포함, 시즌 열기",
                season.AutomationName);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void SortDirection_DefaultsToAscendingAndRemainsSelectedAcrossCriteria()
    {
        var root = Directory.CreateTempSubdirectory("dabom-sort-");
        try
        {
            var alpha = Path.Combine(root.FullName, "Alpha.mkv");
            var bravo = Path.Combine(root.FullName, "Bravo.mkv");
            var charlie = Path.Combine(root.FullName, "Charlie.mkv");
            var alphaTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var bravoTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var charlieTime = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var store = new LibraryStore(root.FullName);
            var vm = CreateViewModel(store, new StubScanner(), new LibraryData());
            vm.Videos.Add(new VideoItemViewModel(charlie, new VideoRecord
            {
                Title = "Charlie",
                ReleaseDate = new DateOnly(2020, 1, 1),
                LastWriteTimeUtc = charlieTime
            }, store));
            vm.Videos.Add(new VideoItemViewModel(alpha, new VideoRecord
            {
                Title = "Alpha",
                ReleaseDate = new DateOnly(2024, 1, 1),
                LastWriteTimeUtc = alphaTime
            }, store));
            vm.Videos.Add(new VideoItemViewModel(bravo, new VideoRecord
            {
                Title = "Bravo",
                ReleaseDate = new DateOnly(2022, 1, 1),
                LastWriteTimeUtc = bravoTime
            }, store));

            string[] Titles() => vm.VisibleVideos.Cast<VideoItemViewModel>()
                .Select(video => video.DisplayTitle)
                .ToArray();

            var initialTitles = Titles();
            Assert.AreEqual(
                3,
                initialTitles.Length,
                $"실제 제목: {string.Join(", ", initialTitles)}; 상태: {vm.StatusMessage}");
            CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, initialTitles);
            Assert.AreEqual(VideoSort.Title, vm.SelectedSort);
            Assert.IsFalse(vm.IsSortDescending);
            var toggleDirection = typeof(MainViewModel)
                .GetProperty("ToggleSortDirectionCommand")?
                .GetValue(vm) as System.Windows.Input.ICommand;
            Assert.IsNotNull(toggleDirection, "정렬 방향 전환 명령이 필요합니다.");

            toggleDirection.Execute(null);
            CollectionAssert.AreEqual(new[] { "Charlie", "Bravo", "Alpha" }, Titles());

            vm.SelectedSort = VideoSort.ReleaseDate;
            CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, Titles());
            Assert.IsTrue(vm.IsSortDescending);

            toggleDirection.Execute(null);
            CollectionAssert.AreEqual(new[] { "Charlie", "Bravo", "Alpha" }, Titles());

            vm.SelectedSort = VideoSort.FileModified;
            CollectionAssert.AreEqual(new[] { "Alpha", "Charlie", "Bravo" }, Titles());
            Assert.IsFalse(vm.IsSortDescending);
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
    public void RemoveLocation_WhenFollowUpScanFails_HidesInactiveVideoAndPreservesFiles()
    {
        RunOnDispatcher(async () =>
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
                var vm = CreateViewModel(
                    store,
                    new SuccessThenFailScanner(Scan(videoPath)),
                    data);
                await vm.InitializeAsync(null);
                vm.SelectedVideo = vm.Videos.Single();

                Assert.IsTrue(await vm.RemoveLocationAsync(location), vm.StatusMessage);

                Assert.AreEqual(0, vm.Videos.Count);
                Assert.IsNull(vm.SelectedVideo);
                Assert.IsNull(vm.FeaturedVideo);
                var reloaded = await new LibraryStore(dataRoot).LoadAsync(CancellationToken.None);
                Assert.IsTrue(reloaded.VideosByPath.ContainsKey(Path.GetFullPath(videoPath)));
                Assert.IsTrue(File.Exists(videoPath));
                Assert.IsTrue(File.Exists(posterPath));
                StringAssert.Contains(vm.StatusMessage, "scan failed");
            }
            finally
            {
                root.Delete(true);
            }
        });
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
    public async Task ScanAsync_MissingAccessibleVideoStaysVisibleAndRequestsOneToast()
    {
        var root = Directory.CreateTempSubdirectory("dabom-missing-video-");
        try
        {
            var location = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Movies")).FullName;
            var path = Path.Combine(location, "Missing.mkv");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(),
                CachedData(location, path));
            var toasts = new List<ToastRequest>();
            vm.ToastRequested += (_, toast) => toasts.Add(toast);

            await vm.ScanAsync();

            var video = vm.Videos.Single();
            Assert.AreEqual(VideoFileStatus.Missing, video.FileStatus);
            Assert.IsTrue(video.IsFileMissing);
            Assert.AreEqual(0.5, video.PosterOpacity);
            Assert.AreEqual("Missing, 영상, 파일 없음", video.AutomationName);
            var toast = toasts.Single();
            Assert.AreEqual(
                $"“{Path.GetFileName(path)}” 파일이 존재하지 않습니다.",
                toast.Message);
            Assert.AreEqual("파일 없음", toast.Result);
            Assert.AreSame(video, toast.Video);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_UnavailablePathDoesNotBecomeMissing()
    {
        var root = Directory.CreateTempSubdirectory("dabom-unavailable-video-");
        try
        {
            var location = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Movies")).FullName;
            var unavailable = Path.Combine(location, "Offline");
            var path = Path.Combine(unavailable, "Movie.mkv");
            var result = Scan() with { UnavailablePaths = [unavailable] };
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new SequenceScanner(result),
                CachedData(location, path));
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.ScanAsync();

            Assert.AreEqual(VideoFileStatus.Unavailable, vm.Videos.Single().FileStatus);
            Assert.IsFalse(vm.Videos.Single().IsFileMissing);
            CollectionAssert.AreEqual(
                new[] { $"보관 위치에 연결할 수 없습니다: {location}" },
                toasts);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_MissingVideoFoundAgainClearsStateWithoutAddedToast()
    {
        var root = Directory.CreateTempSubdirectory("dabom-restored-video-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new SequenceScanner(Scan(), Scan(path)),
                CachedData(root.FullName, path));
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.ScanAsync();
            Assert.AreEqual(VideoFileStatus.Missing, vm.Videos.Single().FileStatus);

            await vm.ScanAsync();

            Assert.AreEqual(VideoFileStatus.Present, vm.Videos.Single().FileStatus);
            CollectionAssert.AreEqual(
                new[] { $"“{Path.GetFileName(path)}” 파일이 존재하지 않습니다." },
                toasts);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenInitialNewRecordSaveFailsDoesNotConfirmVideoAndRequestsOneToast()
    {
        var root = Directory.CreateTempSubdirectory("dabom-new-save-failure-");
        try
        {
            var path = Path.Combine(root.FullName, "New.mkv");
            var data = new LibraryData { Locations = [root.FullName] };
            await new LibraryStore(root.FullName).SaveAsync(data);
            var store = new LibraryStore(
                root.FullName,
                (_, _, _) => Task.FromException(new IOException("disk full")));
            var metadataStarted = false;
            var provider = new TestProvider(
                (_, _) =>
                {
                    metadataStarted = true;
                    return Task.FromResult<IReadOnlyList<MetadataCandidate>>([]);
                },
                (_, _) => throw new InvalidOperationException());
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.ScanAsync();

            Assert.AreEqual(0, vm.Videos.Count);
            var reloaded = await new LibraryStore(root.FullName)
                .LoadAsync(CancellationToken.None);
            Assert.IsFalse(reloaded.VideosByPath.ContainsKey(Path.GetFullPath(path)));
            Assert.IsFalse(metadataStarted);
            CollectionAssert.AreEqual(
                new[] { $"“{Path.GetFileName(path)}”을 라이브러리에 저장하지 못했습니다." },
                toasts);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void ScanAsync_NewVideo_WithCompleteMetadataRequestsSuccessToast()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory(
                "dabom-new-metadata-success-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var sourcePoster = Path.Combine(root.FullName, "source.png");
                WritePng(sourcePoster);
                using var imageClient = new HttpClient(new ResponseHandler(_ =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(File.ReadAllBytes(sourcePoster))
                    }));
                var store = new LibraryStore(root.FullName);
                var provider = TestProvider.ForMovie(MovieDetails(
                    "메타데이터 제목",
                    "1",
                    new Uri("https://image.tmdb.org/poster.png")));
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    new LibraryData { Locations = [root.FullName] });
                var toasts = new List<string>();
                vm.ToastRequested += (_, message) => toasts.Add(message.Message);

                await vm.ScanAsync();

                CollectionAssert.AreEqual(
                    new[] { "메타데이터 적용 완료 · 성공 1 · 결과 없음 0 · 실패 0" },
                    toasts);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void ScanAsync_NewVideos_RequestOneFinalSummaryToast()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory(
                "dabom-new-metadata-failure-");
            try
            {
                var first = Path.Combine(root.FullName, "First.Movie.mkv");
                var second = Path.Combine(root.FullName, "Second.Movie.mkv");
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient();
                var provider = TestProvider.ForMovie(
                    MovieDetails("메타데이터 제목", "1"));
                var vm = new MainViewModel(
                    store,
                    new StubScanner(first, second),
                    CreateEnrichment(store, imageClient, provider),
                    new LibraryData { Locations = [root.FullName] });
                var toasts = new List<string>();
                vm.ToastRequested += (_, message) => toasts.Add(message.Message);

                await vm.ScanAsync();

                CollectionAssert.AreEqual(
                    new[] { "메타데이터 적용 완료 · 성공 0 · 결과 없음 0 · 실패 2" },
                    toasts);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void ScanAsync_NewVideo_WhenMetadataCommitFailsRequestsOnlySaveFailureToast()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory(
                "dabom-new-metadata-save-failure-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var sourcePoster = Path.Combine(root.FullName, "source.png");
                WritePng(sourcePoster);
                var commits = 0;
                var store = new LibraryStore(
                    root.FullName,
                    (temporary, destination, _) =>
                    {
                        commits++;
                        if (commits == 2) throw new IOException("disk full");
                        File.Move(temporary, destination);
                        return Task.CompletedTask;
                    });
                using var imageClient = new HttpClient(new ResponseHandler(_ =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(File.ReadAllBytes(sourcePoster))
                    }));
                var provider = TestProvider.ForMovie(MovieDetails(
                    "메타데이터 제목",
                    "1",
                    new Uri("https://image.tmdb.org/poster.png")));
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    new LibraryData { Locations = [root.FullName] });
                var toasts = new List<string>();
                vm.ToastRequested += (_, message) => toasts.Add(message.Message);

                await vm.ScanAsync();

                Assert.AreEqual(
                    MetadataStatus.Pending,
                    vm.Videos.Single().Record.MetadataStatus);
                var reloaded = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.AreEqual(
                    MetadataStatus.Pending,
                    reloaded.VideosByPath[Path.GetFullPath(path)].MetadataStatus);
                Assert.AreEqual(0, Directory.EnumerateFiles(store.PostersPath).Count());
                CollectionAssert.AreEqual(
                    new[]
                    {
                        $"“{Path.GetFileName(path)}”을 라이브러리에 저장하지 못했습니다.",
                        "메타데이터 적용 완료 · 성공 0 · 결과 없음 0 · 실패 1"
                    },
                    toasts);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task ScanAsync_ExistingFailedRetryDoesNotRequestResultToast()
    {
        var root = Directory.CreateTempSubdirectory("dabom-existing-metadata-retry-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                MetadataStatus = MetadataStatus.Failed
            };
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var provider = TestProvider.ForMovie(MovieDetails("메타데이터 제목", "1"));
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data);
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.ScanAsync();

            Assert.AreEqual(MetadataStatus.Matched, vm.Videos.Single().Record.MetadataStatus);
            Assert.AreEqual(0, toasts.Count);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenNoMetadataTargets_DoesNotRequestResultToast()
    {
        var root = Directory.CreateTempSubdirectory("dabom-no-metadata-targets-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                MetadataStatus = MetadataStatus.Matched
            };
            var store = new LibraryStore(root.FullName);
            using var imageClient = new HttpClient();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(
                    store,
                    imageClient,
                    TestProvider.ForMovie(MovieDetails("메타데이터 제목", "1"))),
                data);
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.ScanAsync();

            Assert.AreEqual(0, toasts.Count);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void ScanAsync_AfterScanEnrichesPendingAndFailedRecords()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-scan-enrich-");
            try
            {
                var first = Path.Combine(root.FullName, "First.Movie.mkv");
                var second = Path.Combine(root.FullName, "Second.Movie.mkv");
                var sourcePoster = Path.Combine(root.FullName, "source.png");
                WritePng(sourcePoster);
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
                        lock (queries) queries.Add(query.Title);
                        return Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                        [
                            new("test", "movie", query.Title, MediaType.Movie)
                        ]);
                    },
                    (candidate, _) => Task.FromResult(
                        MovieDetails(
                            $"적용 {candidate.ResourceId}",
                            candidate.ResourceId,
                            new Uri("https://image.tmdb.org/poster.png"))));
                var store = new LibraryStore(root.FullName);
                using var imageClient = new HttpClient(new ResponseHandler(_ =>
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(File.ReadAllBytes(sourcePoster))
                    }));
                var vm = new MainViewModel(
                    store,
                    new StubScanner(first, second),
                    CreateEnrichment(store, imageClient, provider),
                    data);

                await vm.ScanAsync();

                CollectionAssert.AreEquivalent(
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
    public void ScanAsync_PersistsEachSuccessButUpdatesUiOnlyAfterAllItemsFinish()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-enrich-batch-ui-");
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var first = Path.Combine(root.FullName, "First.Movie.mkv");
                var second = Path.Combine(root.FullName, "Second.Movie.mkv");
                var data = CachedData(root.FullName, first, second);
                data.VideosByPath[first] = data.VideosByPath[first] with
                {
                    Title = "첫 이전 제목",
                    MetadataStatus = MetadataStatus.Pending
                };
                data.VideosByPath[second] = data.VideosByPath[second] with
                {
                    Title = "둘 이전 제목",
                    MetadataStatus = MetadataStatus.Pending
                };
                await new LibraryStore(root.FullName).SaveAsync(data);
                var outOfOrderSaved = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseStoredCommit = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var delayedStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseDelayed = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var commits = 0;
                var store = new LibraryStore(
                    root.FullName,
                    async (temporary, destination, _) =>
                    {
                        File.Move(temporary, destination, true);
                        if (Interlocked.Increment(ref commits) == 1)
                        {
                            outOfOrderSaved.TrySetResult();
                            await releaseStoredCommit.Task;
                        }
                    });
                var provider = new TestProvider(
                    (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                    [
                        new("test", "movie", query.Title, MediaType.Movie)
                    ]),
                    async (candidate, token) =>
                    {
                        if (candidate.ResourceId == "First Movie")
                        {
                            delayedStarted.TrySetResult();
                            await releaseDelayed.Task.WaitAsync(token);
                        }
                        return MovieDetails(
                            $"새 {candidate.ResourceId}",
                            candidate.ResourceId);
                    });
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(first, second),
                    CreateEnrichment(store, imageClient, provider),
                    data);
                var resets = 0;

                var scan = vm.ScanAsync();
                await delayedStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await outOfOrderSaved.Task.WaitAsync(TimeSpan.FromSeconds(2));
                vm.VisibleItems.CollectionChanged += (_, args) =>
                {
                    Assert.IsTrue(dispatcher.CheckAccess());
                    if (args.Action ==
                        System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                    {
                        resets++;
                    }
                };

                var persisted = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.AreEqual(
                    1,
                    persisted.VideosByPath.Values.Count(record =>
                        record.MetadataStatus == MetadataStatus.Matched));
                Assert.IsTrue(vm.Videos.All(video =>
                    video.Record.MetadataStatus == MetadataStatus.Pending));

                releaseStoredCommit.TrySetResult();
                await Dispatcher.CurrentDispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Background);
                Assert.IsTrue(vm.Videos.All(video =>
                    video.Record.MetadataStatus == MetadataStatus.Pending));

                releaseDelayed.TrySetResult();
                await scan;

                Assert.IsTrue(vm.Videos.All(video =>
                    video.Record.MetadataStatus == MetadataStatus.Matched));
                Assert.AreEqual(1, resets);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void ScanAsync_MetadataCoordinatorRunsOffUiThread()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-enrich-dispatcher-");
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                data.VideosByPath[path] = data.VideosByPath[path] with
                {
                    MetadataStatus = MetadataStatus.Pending
                };
                var started = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var provider = new TestProvider(
                    async (_, token) =>
                    {
                        Assert.IsFalse(dispatcher.CheckAccess());
                        started.TrySetResult();
                        await release.Task.WaitAsync(token);
                        return [];
                    },
                    (_, _) => throw new AssertFailedException(
                        "상세 조회를 호출하면 안 됩니다."));
                var store = new LibraryStore(
                    root.FullName,
                    (temporary, destination, _) =>
                    {
                        Assert.IsFalse(dispatcher.CheckAccess());
                        File.Move(temporary, destination, true);
                        return Task.CompletedTask;
                    });
                using var imageClient = new HttpClient();
                var vm = new MainViewModel(
                    store,
                    new StubScanner(path),
                    CreateEnrichment(store, imageClient, provider),
                    data);

                var scan = vm.ScanAsync();
                await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var inputRan = false;
                await dispatcher.InvokeAsync(
                    () => inputRan = true,
                    DispatcherPriority.Input);
                Assert.IsTrue(inputRan);
                Assert.IsFalse(scan.IsCompleted);

                release.TrySetResult();
                await scan;
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task ScanAsync_LifetimeCancellationReachesMetadataWork()
    {
        var root = Directory.CreateTempSubdirectory("dabom-enrich-lifetime-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            data.VideosByPath[path] = data.VideosByPath[path] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            var store = new LibraryStore(root.FullName);
            await store.SaveAsync(data);
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new TestProvider(
                async (_, token) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return [];
                },
                (_, _) => throw new AssertFailedException(
                    "상세 조회를 호출하면 안 됩니다."));
            using var imageClient = new HttpClient();
            using var lifetime = new CancellationTokenSource();
            var vm = new MainViewModel(
                store,
                new StubScanner(path),
                CreateEnrichment(store, imageClient, provider),
                data,
                lifetime.Token);

            var scan = vm.ScanAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            lifetime.Cancel();
            await scan.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.AreEqual(
                MetadataStatus.Pending,
                vm.Videos.Single().Record.MetadataStatus);
            var reloaded = await new LibraryStore(root.FullName)
                .LoadAsync(CancellationToken.None);
            Assert.AreEqual(
                MetadataStatus.Pending,
                reloaded.VideosByPath[path].MetadataStatus);
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
            var third = Path.Combine(root.FullName, "Third.Movie.mkv");
            var data = CachedData(root.FullName, first, second, third);
            data.VideosByPath[first] = data.VideosByPath[first] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            data.VideosByPath[second] = data.VideosByPath[second] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            data.VideosByPath[third] = data.VideosByPath[third] with
            {
                MetadataStatus = MetadataStatus.Pending
            };
            var commits = 0;
            var store = new LibraryStore(
                root.FullName,
                (temporary, destination, _) =>
                {
                    commits++;
                    if (commits == 2) throw new IOException("disk full");
                    File.Move(temporary, destination, true);
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
                new StubScanner(first, second, third),
                CreateEnrichment(store, imageClient, provider),
                data);

            await vm.ScanAsync();

            Assert.AreEqual(3, commits);
            Assert.AreEqual(
                2,
                vm.Videos.Count(video =>
                    video.Record.MetadataStatus == MetadataStatus.Matched));
            Assert.AreEqual(
                1,
                vm.Videos.Count(video =>
                    video.Record.MetadataStatus == MetadataStatus.Pending));
            var reloaded = await new LibraryStore(root.FullName)
                .LoadAsync(CancellationToken.None);
            Assert.AreEqual(
                2,
                reloaded.VideosByPath.Values.Count(record =>
                    record.MetadataStatus == MetadataStatus.Matched));
            Assert.AreEqual(
                1,
                reloaded.VideosByPath.Values.Count(record =>
                    record.MetadataStatus == MetadataStatus.Pending));
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
    public void ScanAsync_ShowsFoundCountBeforeTheFinalSummary()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-scan-count-");
            try
            {
                var first = Path.Combine(root.FullName, "First.mkv");
                var second = Path.Combine(root.FullName, "Second.mkv");
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName),
                    new DeferredProgressScanner(first, second),
                    CachedData(root.FullName, first, second));
                var messages = new List<string>();
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.StatusMessage))
                    {
                        messages.Add(vm.StatusMessage);
                    }
                };

                await vm.ScanAsync();

                CollectionAssert.Contains(
                    messages,
                    "폴더 확인 중 · 영상 1개 확인");
                Assert.AreEqual("폴더 확인을 마쳤습니다.", vm.StatusMessage);
                Assert.AreEqual("폴더 확인을 마쳤습니다.", messages.Last());
            }
            finally
            {
                root.Delete(true);
            }
        });
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
                vm.IsSortDescending = true;
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
    public void MetadataSave_MovesEpisodeAndClosesDissolvedOpenSeason()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-season-edit-");
            try
            {
                var first = Path.Combine(root.FullName, "S01E01.mkv");
                var moved = Path.Combine(root.FullName, "S01E02.mkv");
                var target = Path.Combine(root.FullName, "S02E01.mkv");
                var data = CachedData(root.FullName, first, moved, target);
                data.VideosByPath[first] = TvRecord("S01E01", "시리즈", 1, 1, "10");
                data.VideosByPath[moved] = TvRecord("S01E02", "시리즈", 1, 2, "10");
                data.VideosByPath[target] = TvRecord("S02E01", "시리즈", 2, 1, "10");
                var vm = CreateViewModel(
                    new LibraryStore(root.FullName),
                    new StubScanner(first, moved, target),
                    data);
                await vm.ScanAsync();
                var featured = vm.FeaturedVideo;
                var seasonOne = vm.VisibleItems
                    .OfType<SeasonItemViewModel>()
                    .Single(season => season.SeasonNumber == 1);
                Assert.IsTrue(vm.OpenSeason(seasonOne));
                vm.SelectedVideo = vm.Videos.Single(video => video.Path == moved);
                var editor = vm.CreateMetadataEditor()!;
                editor.SeasonNumberText = "2";

                Assert.IsTrue(await editor.SaveAsync());

                Assert.IsFalse(vm.IsSeasonView);
                Assert.IsNull(vm.SelectedVideo);
                Assert.AreSame(featured, vm.FeaturedVideo);
                Assert.AreEqual(
                    2,
                    vm.VisibleItems.OfType<SeasonItemViewModel>()
                        .Single().EpisodeCount);
                Assert.AreEqual(
                    2,
                    vm.Videos.Single(video => video.Path == moved).Record.SeasonNumber);
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
                Assert.IsTrue(vm.Videos.Single().HasPoster);
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
    public async Task InitializeAsync_UsesTheSameMissingFileAndToastRules()
    {
        var root = Directory.CreateTempSubdirectory("dabom-initialize-missing-");
        try
        {
            var path = Path.Combine(root.FullName, "Missing.mkv");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(),
                CachedData(root.FullName, path));
            var toasts = new List<string>();
            vm.ToastRequested += (_, message) => toasts.Add(message.Message);

            await vm.InitializeAsync(null);

            Assert.AreEqual(VideoFileStatus.Missing, vm.Videos.Single().FileStatus);
            CollectionAssert.AreEqual(
                new[] { $"“{Path.GetFileName(path)}” 파일이 존재하지 않습니다." },
                toasts);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task VideoItem_LoadsPosterAfterConstructionWhenRequested()
    {
        var root = Directory.CreateTempSubdirectory("dabom-deferred-poster-");
        try
        {
            var posters = Directory.CreateDirectory(
                Path.Combine(root.FullName, "posters"));
            WritePng(Path.Combine(posters.FullName, "movie.png"));
            var item = new VideoItemViewModel(
                @"D:\Movie.mkv",
                new VideoRecord { Poster = "posters/movie.png" },
                new LibraryStore(root.FullName));

            Assert.IsNull(item.Poster);
            Assert.IsFalse(item.HasPoster);

            await item.LoadPosterAsync();

            Assert.IsNotNull(item.Poster);
            Assert.IsTrue(item.HasPoster);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task VideoItem_RetriesPosterAfterMissingFileAppears()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-retry-");
        try
        {
            var posters = Directory.CreateDirectory(
                Path.Combine(root.FullName, "posters"));
            var posterPath = Path.Combine(posters.FullName, "movie.png");
            var item = new VideoItemViewModel(
                @"D:\Movie.mkv",
                new VideoRecord { Poster = "posters/movie.png" },
                new LibraryStore(root.FullName));

            await item.LoadPosterAsync();
            Assert.IsFalse(item.HasPoster);

            WritePng(posterPath);
            await item.LoadPosterAsync();

            Assert.IsTrue(item.HasPoster);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadPostersAsync_SkipsVideosNoLongerInTheLibrary()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-skip-");
        try
        {
            var posters = Directory.CreateDirectory(
                Path.Combine(root.FullName, "posters"));
            WritePng(Path.Combine(posters.FullName, "current.png"));
            WritePng(Path.Combine(posters.FullName, "removed.png"));
            var store = new LibraryStore(root.FullName);
            var current = new VideoItemViewModel(
                @"D:\Current.mkv",
                new VideoRecord { Poster = "posters/current.png" },
                store);
            var removed = new VideoItemViewModel(
                @"D:\Removed.mkv",
                new VideoRecord { Poster = "posters/removed.png" },
                store);
            var vm = CreateViewModel(store, new StubScanner(), new LibraryData());
            vm.Videos.Add(current);

            await vm.LoadPostersAsync([current, removed]);

            Assert.IsTrue(current.HasPoster);
            Assert.IsFalse(removed.HasPoster);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_ShowsActiveCachedVideosBeforeScanCompletes()
    {
        var root = Directory.CreateTempSubdirectory("dabom-initialize-cache-");
        try
        {
            var location = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Movies")).FullName;
            var cachedPath = Path.Combine(location, "Cached.mkv");
            var inactivePath = Path.Combine(root.FullName, "Old", "Inactive.mkv");
            var scanner = new BlockingScanner(cachedPath);
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                scanner,
                CachedData(location, cachedPath, inactivePath));
            Assert.IsTrue(vm.IsLibraryLoading);
            var initialization = vm.InitializeAsync(null);

            try
            {
                await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

                Assert.IsFalse(initialization.IsCompleted);
                Assert.AreEqual(1, vm.Videos.Count);
                Assert.AreEqual(Path.GetFullPath(cachedPath), vm.Videos.Single().Path);
                Assert.AreEqual(1, vm.DisplayItemCount);
                Assert.AreSame(vm.Videos.Single(), vm.FeaturedVideo);
                Assert.IsTrue(vm.IsLibraryLoading);
            }
            finally
            {
                scanner.Complete();
                await initialization;
            }

            Assert.IsFalse(vm.IsLibraryLoading);
            Assert.IsTrue(vm.HasCompletedLibraryScan);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_PreservesCachedFeaturedAfterScanCompletes()
    {
        var root = Directory.CreateTempSubdirectory("dabom-featured-cache-");
        try
        {
            var location = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Movies")).FullName;
            var firstPath = Path.Combine(location, "First.mkv");
            var secondPath = Path.Combine(location, "Second.mkv");
            var scanner = new BlockingScanner(firstPath, secondPath);
            var pickCalls = 0;
            var vm = new MainViewModel(
                new LibraryStore(root.FullName),
                scanner,
                CachedData(location, firstPath, secondPath),
                _ => true,
                () => DateTimeOffset.UtcNow,
                maximum => pickCalls++ == 0 ? 0 : maximum - 1);
            var initialization = vm.InitializeAsync(null);

            await scanner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var cachedFeatured = vm.FeaturedVideo;
            scanner.Complete();
            await initialization;

            Assert.AreEqual(Path.GetFullPath(firstPath), cachedFeatured!.Path);
            Assert.AreSame(cachedFeatured, vm.FeaturedVideo);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenRefreshFails_PreservesLastCompletedLibraryState()
    {
        var root = Directory.CreateTempSubdirectory("dabom-scan-state-");
        try
        {
            var location = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Movies")).FullName;
            var path = Path.Combine(location, "Movie.mkv");
            var warning = new ScanWarning(location, "첫 경고");
            var vm = CreateViewModel(
                new LibraryStore(root.FullName),
                new SuccessThenFailScanner(new(Scan(path).Videos, [warning])),
                CachedData(location, path));
            await vm.InitializeAsync(null);
            Assert.IsTrue(vm.HasCompletedLibraryScan);
            var video = vm.Videos.Single();
            var warnings = vm.Warnings.ToArray();
            var lastScanUtc = vm.LastScanUtc;

            await vm.ScanAsync();

            Assert.IsFalse(vm.IsLibraryLoading);
            Assert.IsTrue(vm.HasCompletedLibraryScan);
            Assert.AreSame(video, vm.Videos.Single());
            Assert.AreEqual(VideoFileStatus.Present, video.FileStatus);
            CollectionAssert.AreEqual(warnings, vm.Warnings.ToArray());
            Assert.AreEqual(lastScanUtc, vm.LastScanUtc);
            StringAssert.Contains(vm.StatusMessage, "scan failed");
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
            Assert.AreEqual(1, vm.Videos.Count);
            Assert.AreEqual(VideoFileStatus.Missing, vm.Videos.Single().FileStatus);
            Assert.IsFalse(vm.IsFilterEmptyStateVisible);

            await vm.ScanAsync();

            Assert.AreEqual("드라마", vm.SelectedFilter!.Genre);
            Assert.AreEqual(1, vm.VisibleCount);
            Assert.AreEqual(VideoFileStatus.Present, vm.Videos.Single().FileStatus);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void PrepareVideoDeletions_PreservesVideoOrderAndExcludesSeasonAndUnsafeItem()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-prepare-many-");
        try
        {
            var presentPath = Path.Combine(root.FullName, "Present.mkv");
            var missingPath = Path.Combine(root.FullName, "Missing.mkv");
            var unsafePath = Path.Combine(root.FullName, "Unsafe.mkv");
            var store = new LibraryStore(root.FullName);
            var data = CachedData(root.FullName, presentPath, missingPath, unsafePath);
            var identity = new FileIdentity(7, 10, 20);
            var probes = new Dictionary<string, FileProbeResult>(StringComparer.OrdinalIgnoreCase)
            {
                [presentPath] = new(VideoFileStatus.Present, identity),
                [missingPath] = new(VideoFileStatus.Missing, null),
                [unsafePath] = new(VideoFileStatus.Unavailable, null)
            };
            var viewModel = new MainViewModel(
                store,
                new StubScanner(),
                data,
                _ => true,
                () => DateTimeOffset.UtcNow,
                _ => 0,
                null,
                path => probes[path],
                _ => Assert.Fail());
            foreach (var path in new[] { presentPath, missingPath, unsafePath })
            {
                viewModel.Videos.Add(new VideoItemViewModel(
                    path,
                    data.VideosByPath[path],
                    store));
            }
            var episode = new VideoItemViewModel(
                Path.Combine(root.FullName, "Episode.mkv"),
                TvRecord("에피소드", "시리즈", 1, 1),
                store);
            var season = new SeasonItemViewModel(
                SeasonGroupKey.From(episode.Record)!,
                [episode],
                [episode]);
            var toasts = new List<ToastRequest>();
            viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

            var preparation = viewModel.PrepareVideoDeletions(
                [viewModel.Videos[0], season, viewModel.Videos[1], viewModel.Videos[2]]);

            Assert.IsNotNull(preparation);
            CollectionAssert.AreEqual(
                new[] { presentPath, missingPath },
                preparation.Requests.Select(request => request.Video.Path).ToArray());
            CollectionAssert.AreEqual(
                new[] { VideoFileStatus.Present, VideoFileStatus.Missing },
                preparation.Requests.Select(request => request.Status).ToArray());
            Assert.AreEqual(1, preparation.Failures.Count);
            Assert.AreSame(viewModel.Videos[2], preparation.Failures[0].Video);
            Assert.AreEqual(VideoDeletionFailureKind.FileStatus, preparation.Failures[0].Kind);
            Assert.AreEqual(3, preparation.VideoCount);
            Assert.AreEqual(1, toasts.Count);
            StringAssert.Contains(toasts[0].Message, "TV 시즌은 한 번에 삭제할 수 없습니다");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void DeleteVideos_ContinuesAfterEveryFailureAndReportsOneSummary()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-delete-many-failures-");
            try
            {
            var preparationFailurePath = Path.Combine(root.FullName, "Prepare.mkv");
            var changedPath = Path.Combine(root.FullName, "Changed.mkv");
            var recycleFailurePath = Path.Combine(root.FullName, "Recycle.mkv");
            var listFailurePath = Path.Combine(root.FullName, "List.mkv");
            var movedListFailurePath = Path.Combine(root.FullName, "Moved.mkv");
            var successPath = Path.Combine(root.FullName, "Success.mkv");
            var paths = new[]
            {
                preparationFailurePath,
                changedPath,
                recycleFailurePath,
                listFailurePath,
                movedListFailurePath,
                successPath
            };
            var data = CachedData(root.FullName, paths);
            var firstIdentity = new FileIdentity(7, 10, 20);
            var secondIdentity = new FileIdentity(7, 11, 20);
            var thirdIdentity = new FileIdentity(7, 12, 20);
            var probes = new Dictionary<string, Queue<FileProbeResult>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [preparationFailurePath] = new([
                    new(VideoFileStatus.Unknown, null)]),
                [changedPath] = new([
                    new(VideoFileStatus.Present, firstIdentity),
                    new(VideoFileStatus.Missing, null)]),
                [recycleFailurePath] = new([
                    new(VideoFileStatus.Present, secondIdentity),
                    new(VideoFileStatus.Present, secondIdentity)]),
                [listFailurePath] = new([
                    new(VideoFileStatus.Missing, null),
                    new(VideoFileStatus.Missing, null)]),
                [movedListFailurePath] = new([
                    new(VideoFileStatus.Present, thirdIdentity),
                    new(VideoFileStatus.Present, thirdIdentity)]),
                [successPath] = new([
                    new(VideoFileStatus.Missing, null),
                    new(VideoFileStatus.Missing, null)])
            };
            var saveCalls = 0;
            var store = new LibraryStore(root.FullName, (temporary, destination, _) =>
            {
                saveCalls++;
                if (saveCalls <= 2) throw new IOException("disk full");
                File.Move(temporary, destination, true);
                return Task.CompletedTask;
            });
            var recycled = new List<string>();
            var viewModel = new MainViewModel(
                store,
                new StubScanner(),
                data,
                _ => true,
                () => DateTimeOffset.UtcNow,
                _ => 0,
                null,
                path => probes[path].Dequeue(),
                path =>
                {
                    if (path == recycleFailurePath) throw new IOException("recycle failed");
                    recycled.Add(path);
                });
            foreach (var path in paths)
            {
                viewModel.Videos.Add(new VideoItemViewModel(
                    path,
                    data.VideosByPath[path],
                    store));
            }
            var toasts = new List<ToastRequest>();
            viewModel.ToastRequested += (_, toast) => toasts.Add(toast);
            var preparation = viewModel.PrepareVideoDeletions(
                viewModel.Videos.Cast<LibraryItemViewModel>().ToArray());

            Assert.IsNotNull(preparation);
            Assert.AreEqual(1, preparation.Failures.Count);
            Assert.AreEqual(0, toasts.Count);

            var result = await viewModel.DeleteVideosAsync(preparation);

            Assert.AreEqual(1, result.DeletedCount);
            Assert.AreEqual(5, result.Failures.Count);
            Assert.AreEqual(
                2,
                result.Failures.Count(failure =>
                    failure.Kind == VideoDeletionFailureKind.FileStatus));
            Assert.AreEqual(
                1,
                result.Failures.Count(failure =>
                    failure.Kind == VideoDeletionFailureKind.RecycleBin));
            Assert.AreEqual(
                1,
                result.Failures.Count(failure =>
                    failure.Kind == VideoDeletionFailureKind.ListRemoval));
            Assert.AreEqual(
                1,
                result.Failures.Count(failure =>
                    failure.Kind == VideoDeletionFailureKind.RecycledListRemoval));
            CollectionAssert.AreEqual(
                paths[..^1],
                result.FailedVideos.Select(video => video.Path).ToArray());
            Assert.AreEqual(5, viewModel.Videos.Count);
            Assert.IsFalse(viewModel.Videos.Any(video => video.Path == successPath));
            Assert.AreEqual(
                VideoFileStatus.Missing,
                viewModel.Videos.Single(video =>
                    video.Path == movedListFailurePath).FileStatus);
            CollectionAssert.AreEqual(new[] { movedListFailurePath }, recycled);
            Assert.AreEqual(3, saveCalls);
            Assert.AreEqual(1, toasts.Count);
            StringAssert.Contains(toasts[0].Message, "삭제 1개, 실패 5개");
            StringAssert.Contains(toasts[0].Message, "파일 상태 확인 실패 2개");
            StringAssert.Contains(toasts[0].Message, "휴지통 이동 실패 1개");
            StringAssert.Contains(toasts[0].Message, "목록 제거 실패 1개");
            StringAssert.Contains(toasts[0].Message, "파일 이동됨 · 목록 제거 실패 1개");
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void DeleteVideo_PresentSameIdentity_RecyclesSavesAndRemovesFromScreenInOrder()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-delete-present-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                var calls = new List<string>();
                var identity = new FileIdentity(7, 10, 20);
                var probe = ProbeSequence(
                    calls,
                    new FileProbeResult(VideoFileStatus.Present, identity),
                    new FileProbeResult(VideoFileStatus.Present, identity));
                var store = new LibraryStore(root.FullName, (temporary, destination, _) =>
                {
                    calls.Add("save");
                    File.Move(temporary, destination, true);
                    return Task.CompletedTask;
                });
                var vm = await CreateDeletionViewModel(
                    store,
                    new StubScanner(path),
                    data,
                    probe,
                    recycledPath =>
                    {
                        Assert.AreEqual(path, recycledPath);
                        calls.Add("recycle");
                    });
                var video = vm.SelectedVideo!;

                var result = await DeleteSelectedVideoAsync(vm);

                Assert.AreEqual(1, result.DeletedCount);
                Assert.AreEqual(0, result.Failures.Count);
                CollectionAssert.AreEqual(
                    new[] { "probe", "probe", "recycle", "save" },
                    calls);
                Assert.IsFalse(vm.Videos.Contains(video));
                Assert.IsFalse(vm.VisibleItems.Contains(video));
                Assert.IsNull(vm.SelectedVideo);
                var saved = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.IsFalse(saved.VideosByPath.ContainsKey(path));
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void DeleteVideo_Missing_SavesListWithoutRecycle()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-delete-missing-");
            try
            {
                var path = Path.Combine(root.FullName, "Movie.mkv");
                var data = CachedData(root.FullName, path);
                var calls = new List<string>();
                var probe = ProbeSequence(
                    calls,
                    new FileProbeResult(VideoFileStatus.Missing, null),
                    new FileProbeResult(VideoFileStatus.Missing, null));
                var store = new LibraryStore(root.FullName, (temporary, destination, _) =>
                {
                    calls.Add("save");
                    File.Move(temporary, destination, true);
                    return Task.CompletedTask;
                });
                var vm = await CreateDeletionViewModel(
                    store,
                    new StubScanner(path),
                    data,
                    probe,
                    _ => Assert.Fail("누락 파일을 휴지통으로 이동하면 안 됩니다."));
                var toasts = new List<ToastRequest>();
                vm.ToastRequested += (_, toast) => toasts.Add(toast);

                var result = await DeleteSelectedVideoAsync(vm);

                Assert.AreEqual(1, result.DeletedCount);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreEqual(1, toasts.Count);
                Assert.AreEqual("삭제 1개, 실패 0개", toasts[0].Message);
                CollectionAssert.AreEqual(new[] { "probe", "probe", "save" }, calls);
                Assert.AreEqual(0, vm.Videos.Count);
                var saved = await new LibraryStore(root.FullName)
                    .LoadAsync(CancellationToken.None);
                Assert.IsFalse(saved.VideosByPath.ContainsKey(path));
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public void DeleteVideo_NonFeaturedVideo_PreservesFeaturedVideo()
    {
        RunOnDispatcher(async () =>
        {
            var root = Directory.CreateTempSubdirectory("dabom-delete-featured-");
            try
            {
                var first = Path.Combine(root.FullName, "First.mkv");
                var second = Path.Combine(root.FullName, "Second.mkv");
                var third = Path.Combine(root.FullName, "Third.mkv");
                var pickerCalls = 0;
                var store = new LibraryStore(root.FullName);
                var probe = new FileProbeResult(VideoFileStatus.Missing, null);
                var vm = new MainViewModel(
                    store,
                    new StubScanner(first, second, third),
                    CachedData(root.FullName, first, second, third),
                    _ => true,
                    () => DateTimeOffset.UtcNow,
                    _ => pickerCalls++ == 0 ? 0 : 1,
                    null,
                    _ => probe,
                    _ => Assert.Fail("누락 파일을 휴지통으로 이동하면 안 됩니다."));
                await vm.ScanAsync();
                var featured = vm.FeaturedVideo;
                vm.SelectedVideo = vm.Videos[2];

                var result = await DeleteSelectedVideoAsync(vm);

                Assert.AreEqual(1, result.DeletedCount);
                Assert.AreEqual(0, result.Failures.Count);
                Assert.AreSame(featured, vm.FeaturedVideo);
                Assert.AreEqual(1, pickerCalls);
            }
            finally
            {
                root.Delete(true);
            }
        });
    }

    [TestMethod]
    public async Task PrepareVideoDeletion_LastScanPresentButLiveMissing_PreparesListOnlyRequest()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-live-missing-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var calls = new List<string>();
            var vm = await CreateDeletionViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(path),
                CachedData(root.FullName, path),
                ProbeSequence(calls, new FileProbeResult(VideoFileStatus.Missing, null)),
                _ => Assert.Fail());
            Assert.AreEqual(VideoFileStatus.Present, vm.SelectedVideo!.FileStatus);

            var request = vm.PrepareVideoDeletions([vm.SelectedVideo!])!
                .Requests.Single();

            Assert.IsNotNull(request);
            Assert.AreSame(vm.SelectedVideo, request.Video);
            Assert.AreEqual(VideoFileStatus.Missing, request.Status);
            Assert.IsNull(request.Identity);
            CollectionAssert.AreEqual(new[] { "probe" }, calls);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task PrepareVideoDeletion_LastScanMissingButLivePresent_PreparesIdentityRequest()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-live-present-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var identity = new FileIdentity(7, 10, 20);
            var calls = new List<string>();
            var vm = await CreateDeletionViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(),
                CachedData(root.FullName, path),
                ProbeSequence(calls, new FileProbeResult(VideoFileStatus.Present, identity)),
                _ => Assert.Fail());
            Assert.AreEqual(VideoFileStatus.Missing, vm.SelectedVideo!.FileStatus);

            var request = vm.PrepareVideoDeletions([vm.SelectedVideo!])!
                .Requests.Single();

            Assert.IsNotNull(request);
            Assert.AreEqual(VideoFileStatus.Present, request.Status);
            Assert.AreEqual(identity, request.Identity);
            CollectionAssert.AreEqual(new[] { "probe" }, calls);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [DataTestMethod]
    [DataRow(VideoFileStatus.Missing, 7L, 10L, 20L)]
    [DataRow(VideoFileStatus.Present, 8L, 10L, 20L)]
    [DataRow(VideoFileStatus.Present, 7L, 11L, 20L)]
    [DataRow(VideoFileStatus.Present, 7L, 10L, 21L)]
    public async Task DeleteVideo_TargetChanges_DoesNothingAndRequestsRetry(
        VideoFileStatus currentStatus,
        long currentVolume,
        long currentLow,
        long currentHigh)
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-changed-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var calls = new List<string>();
            var currentIdentity = currentStatus == VideoFileStatus.Present
                ? new FileIdentity(
                    (ulong)currentVolume,
                    (ulong)currentLow,
                    (ulong)currentHigh)
                : null;
            var probe = ProbeSequence(
                calls,
                new FileProbeResult(VideoFileStatus.Present, new(7, 10, 20)),
                new FileProbeResult(currentStatus, currentIdentity));
            var store = new LibraryStore(root.FullName, (_, _, _) =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            });
            var vm = await CreateDeletionViewModel(
                store,
                new StubScanner(path),
                CachedData(root.FullName, path),
                probe,
                _ => calls.Add("recycle"));
            string? toast = null;
            vm.ToastRequested += (_, message) => toast = message.Message;

            var result = await DeleteSelectedVideoAsync(vm);

            Assert.AreEqual(0, result.DeletedCount);
            Assert.AreEqual(
                VideoDeletionFailureKind.FileStatus,
                result.Failures.Single().Kind);
            CollectionAssert.AreEqual(new[] { "probe", "probe" }, calls);
            Assert.AreEqual(1, vm.Videos.Count);
            StringAssert.Contains(toast, "삭제 0개, 실패 1개");
            StringAssert.Contains(toast, "파일 상태 확인 실패 1개");
            StringAssert.Contains(
                toast,
                "파일 상태가 변경되어 삭제하지 못했습니다. 다시 시도하세요.");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [DataTestMethod]
    [DataRow(VideoFileStatus.Unknown)]
    [DataRow(VideoFileStatus.Unavailable)]
    [DataRow(VideoFileStatus.Present)]
    public async Task PrepareVideoDeletion_UncertainLiveStatus_RejectsAndRequestsRetry(
        VideoFileStatus status)
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-uncertain-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var calls = new List<string>();
            var vm = await CreateDeletionViewModel(
                new LibraryStore(root.FullName),
                new StubScanner(path),
                CachedData(root.FullName, path),
                ProbeSequence(calls, new FileProbeResult(status, null)),
                _ => Assert.Fail());
            string? toast = null;
            vm.ToastRequested += (_, message) => toast = message.Message;

            var preparation = vm.PrepareVideoDeletions([vm.SelectedVideo!]);

            Assert.IsNull(preparation);
            CollectionAssert.AreEqual(new[] { "probe" }, calls);
            StringAssert.Contains(toast, "삭제 0개, 실패 1개");
            StringAssert.Contains(toast, "파일 상태 확인 실패 1개");
            StringAssert.Contains(toast, "Movie.mkv");
            StringAssert.Contains(
                toast,
                "파일 상태가 변경되어 삭제하지 못했습니다. 다시 시도하세요.");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DeleteVideo_RecycleFails_PreservesJsonAndScreen()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-recycle-fail-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            await new LibraryStore(root.FullName).SaveAsync(data);
            var calls = new List<string>();
            var identity = new FileIdentity(7, 10, 20);
            var store = new LibraryStore(root.FullName, (_, _, _) =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            });
            var vm = await CreateDeletionViewModel(
                store,
                new StubScanner(path),
                data,
                ProbeSequence(
                    calls,
                    new FileProbeResult(VideoFileStatus.Present, identity),
                    new FileProbeResult(VideoFileStatus.Present, identity)),
                _ =>
                {
                    calls.Add("recycle");
                    throw new IOException("recycle failed");
                });
            string? toast = null;
            vm.ToastRequested += (_, message) => toast = message.Message;

            var result = await DeleteSelectedVideoAsync(vm);

            Assert.AreEqual(0, result.DeletedCount);
            Assert.AreEqual(
                VideoDeletionFailureKind.RecycleBin,
                result.Failures.Single().Kind);
            CollectionAssert.AreEqual(new[] { "probe", "probe", "recycle" }, calls);
            Assert.AreEqual(1, vm.Videos.Count);
            var saved = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.IsTrue(saved.VideosByPath.ContainsKey(path));
            StringAssert.Contains(toast, "삭제 0개, 실패 1개");
            StringAssert.Contains(toast, "휴지통 이동 실패 1개");
            StringAssert.Contains(toast, "“Movie.mkv”을 휴지통으로 이동하지 못했습니다.");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DeleteVideo_RecycledButSaveFails_PreservesJsonAndScreenAndMarksRuntimeMissing()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-save-fail-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            await new LibraryStore(root.FullName).SaveAsync(data);
            var calls = new List<string>();
            var identity = new FileIdentity(7, 10, 20);
            var store = new LibraryStore(root.FullName, (_, _, _) =>
            {
                calls.Add("save");
                throw new IOException("disk full");
            });
            var vm = await CreateDeletionViewModel(
                store,
                new StubScanner(path),
                data,
                ProbeSequence(
                    calls,
                    new FileProbeResult(VideoFileStatus.Present, identity),
                    new FileProbeResult(VideoFileStatus.Present, identity)),
                _ => calls.Add("recycle"));
            string? toast = null;
            vm.ToastRequested += (_, message) => toast = message.Message;

            var result = await DeleteSelectedVideoAsync(vm);

            Assert.AreEqual(0, result.DeletedCount);
            Assert.AreEqual(
                VideoDeletionFailureKind.RecycledListRemoval,
                result.Failures.Single().Kind);
            CollectionAssert.AreEqual(
                new[] { "probe", "probe", "recycle", "save" },
                calls);
            Assert.AreEqual(1, vm.Videos.Count);
            Assert.AreEqual(VideoFileStatus.Missing, vm.Videos.Single().FileStatus);
            var saved = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.IsTrue(saved.VideosByPath.ContainsKey(path));
            StringAssert.Contains(toast, "삭제 0개, 실패 1개");
            StringAssert.Contains(toast, "파일 이동됨 · 목록 제거 실패 1개");
            StringAssert.Contains(
                toast,
                "“Movie.mkv” 파일은 이동했지만 영상 목록에서 제거하지 못했습니다.");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DeleteVideo_MissingSaveFails_PreservesJsonAndScreenWithoutRecycle()
    {
        var root = Directory.CreateTempSubdirectory("dabom-delete-list-fail-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            var data = CachedData(root.FullName, path);
            await new LibraryStore(root.FullName).SaveAsync(data);
            var calls = new List<string>();
            var store = new LibraryStore(root.FullName, (_, _, _) =>
            {
                calls.Add("save");
                throw new IOException("disk full");
            });
            var vm = await CreateDeletionViewModel(
                store,
                new StubScanner(path),
                data,
                ProbeSequence(
                    calls,
                    new FileProbeResult(VideoFileStatus.Missing, null),
                    new FileProbeResult(VideoFileStatus.Missing, null)),
                _ => Assert.Fail("누락 파일을 휴지통으로 이동하면 안 됩니다."));
            string? toast = null;
            vm.ToastRequested += (_, message) => toast = message.Message;

            var result = await DeleteSelectedVideoAsync(vm);

            Assert.AreEqual(0, result.DeletedCount);
            Assert.AreEqual(
                VideoDeletionFailureKind.ListRemoval,
                result.Failures.Single().Kind);
            CollectionAssert.AreEqual(new[] { "probe", "probe", "save" }, calls);
            Assert.AreEqual(1, vm.Videos.Count);
            Assert.AreEqual(VideoFileStatus.Present, vm.Videos.Single().FileStatus);
            var saved = await new LibraryStore(root.FullName).LoadAsync(CancellationToken.None);
            Assert.IsTrue(saved.VideosByPath.ContainsKey(path));
            StringAssert.Contains(toast, "삭제 0개, 실패 1개");
            StringAssert.Contains(toast, "목록 제거 실패 1개");
            StringAssert.Contains(
                toast,
                "“Movie.mkv”을 영상 목록에서 제거하지 못했습니다.");
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static VideoDeletionPreparation PrepareSelectedVideo(MainViewModel viewModel) =>
        viewModel.PrepareVideoDeletions([viewModel.SelectedVideo!])!;

    private static Task<VideoDeletionResult> DeleteSelectedVideoAsync(
        MainViewModel viewModel) =>
        viewModel.DeleteVideosAsync(PrepareSelectedVideo(viewModel));

    private static async Task<MainViewModel> CreateDeletionViewModel(
        LibraryStore store,
        ILibraryScanner scanner,
        LibraryData data,
        Func<string, FileProbeResult> probe,
        Action<string> recycle)
    {
        var vm = new MainViewModel(
            store,
            scanner,
            data,
            _ => true,
            () => DateTimeOffset.UtcNow,
            _ => 0,
            null,
            probe,
            recycle);
        await vm.ScanAsync();
        vm.SelectedVideo = vm.Videos.Single();
        return vm;
    }

    private static Func<string, FileProbeResult> ProbeSequence(
        ICollection<string> calls,
        params FileProbeResult[] results)
    {
        var queue = new Queue<FileProbeResult>(results);
        return _ =>
        {
            calls.Add("probe");
            return queue.Dequeue();
        };
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
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            var videos = new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                videos[fullPath] = new(fullPath, 1, DateTimeOffset.UnixEpoch, null);
                progress?.Report(videos.Count);
            }
            return Task.FromResult<ScanResult>(new(videos, []));
        }
    }

    private sealed class BlockingScanner(params string[] paths) : ILibraryScanner
    {
        private readonly TaskCompletionSource<ScanResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            var result = await _completion.Task.WaitAsync(cancellationToken);
            var count = 0;
            foreach (var _ in result.Videos) progress?.Report(++count);
            return result;
        }

        public void Complete() => _completion.TrySetResult(Scan(paths));
    }

    private sealed class TimestampScanner(
        params (string Path, DateTimeOffset LastWriteTimeUtc)[] entries)
        : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            var videos = new Dictionary<string, ScannedVideo>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var path = Path.GetFullPath(entry.Path);
                videos[path] = new(path, 1, entry.LastWriteTimeUtc, null);
                progress?.Report(videos.Count);
            }
            return Task.FromResult(new ScanResult(videos, []));
        }
    }

    private sealed class SequenceScanner(params ScanResult[] results) : ILibraryScanner
    {
        private readonly Queue<ScanResult> _results = new(results);

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            var result = _results.Dequeue();
            for (var count = 1; count <= result.Videos.Count; count++) progress?.Report(count);
            return Task.FromResult(result);
        }
    }

    private sealed class DeferredProgressScanner(params string[] paths) : ILibraryScanner
    {
        public async Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(1);
            await Task.Yield();
            progress?.Report(2);
            return Scan(paths);
        }
    }

    private sealed class SuccessThenFailScanner(ScanResult success) : ILibraryScanner
    {
        private int _calls;

        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            IProgress<int>? progress,
            CancellationToken cancellationToken) =>
            ++_calls == 1
                ? Task.FromResult(success)
                : Task.FromException<ScanResult>(new IOException("scan failed"));
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
