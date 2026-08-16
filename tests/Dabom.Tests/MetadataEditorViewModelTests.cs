using Dabom.Library;
using Dabom.Metadata;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dabom.Tests;

[TestClass]
public sealed class MetadataEditorViewModelTests
{
    [TestMethod]
    public async Task SaveAsync_WhenCommitFails_KeepsDraftOpenForRetry()
    {
        var root = Directory.CreateTempSubdirectory("dabom-editor-failure-");
        try
        {
            var posterPath = Path.Combine(root.FullName, "new.png");
            WritePng(posterPath);
            var record = new VideoRecord { Title = "이전 제목", Actors = ["배우 A"] };
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv", record, null,
                (_, _) => Task.FromResult<string?>("저장 공간 부족"));
            editor.Title = "새 제목";
            editor.ActorsText = "배우 A, 배우 B";
            editor.ChoosePoster(posterPath);
            var preview = editor.PreviewPoster;

            var saved = await editor.SaveAsync();

            Assert.IsFalse(saved);
            Assert.AreEqual("새 제목", editor.Title);
            Assert.AreEqual("배우 A, 배우 B", editor.ActorsText);
            Assert.AreEqual(posterPath, editor.SelectedPosterSourcePath);
            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.AreEqual("저장 공간 부족", editor.ErrorMessage);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_RejectsConcurrentSaveUntilFirstCompletes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commits = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv", new VideoRecord(), null,
            async (_, _) =>
            {
                commits++;
                started.TrySetResult();
                await release.Task;
                return null;
            });

        var first = editor.SaveAsync();
        await started.Task;

        Assert.IsTrue(editor.IsSaving);
        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(1, commits);

        release.TrySetResult();
        Assert.IsTrue(await first);
        Assert.IsFalse(editor.IsSaving);
    }

    [TestMethod]
    public void Constructor_ExposesDraftAndFileInformation()
    {
        var preview = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        preview.Freeze();
        var modified = DateTimeOffset.Parse("2026-07-18T10:30:00+09:00");
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord
            {
                Title = "제목",
                OriginalTitle = "Original",
                ReleaseDate = new DateOnly(2026, 7, 18),
                Director = "감독",
                Actors = ["배우 A", "배우 B"],
                Synopsis = "줄거리",
                FileSizeBytes = 1572864,
                LastWriteTimeUtc = modified,
                DurationTicks = TimeSpan.FromMinutes(91).Ticks
            },
            preview,
            (_, _) => Task.FromResult<string?>(null));

        Assert.AreEqual("제목", editor.Title);
        Assert.AreEqual("제목", editor.SearchText);
        Assert.AreEqual("Original", editor.OriginalTitle);
        Assert.AreEqual(new DateTime(2026, 7, 18), editor.ReleaseDate);
        Assert.AreEqual("감독", editor.Director);
        Assert.AreEqual("배우 A, 배우 B", editor.ActorsText);
        Assert.AreEqual("줄거리", editor.Synopsis);
        Assert.AreEqual("1.5 MB", editor.FileSizeText);
        Assert.AreEqual(modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), editor.ModifiedText);
        Assert.AreEqual("1시간 31분", editor.DurationText);
        Assert.AreSame(preview, editor.PreviewPoster);
        Assert.IsTrue(editor.HasPreviewPoster);
    }

    [TestMethod]
    public async Task ChoosePoster_WhenImageIsInvalid_KeepsCurrentPreview()
    {
        var root = Directory.CreateTempSubdirectory("dabom-editor-invalid-poster-");
        try
        {
            var invalidPath = Path.Combine(root.FullName, "invalid.png");
            await File.WriteAllTextAsync(invalidPath, "not an image");
            var preview = BitmapSource.Create(
                1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            preview.Freeze();
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv", new VideoRecord(), preview,
                (_, _) => Task.FromResult<string?>(null));

            editor.ChoosePoster(invalidPath);

            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.IsNull(editor.SelectedPosterSourcePath);
            StringAssert.Contains(editor.ErrorMessage, "JPG");
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_InitializesQueryAndRejectsBlankInput()
    {
        var calls = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\기생충.mkv",
            new VideoRecord(),
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<MetadataCandidate>>([]);
            },
            (_, _) => throw new AssertFailedException(
                "상세 조회를 호출하면 안 됩니다."));

        Assert.AreEqual("기생충", editor.SearchText);

        editor.SearchText = " ";
        Assert.IsFalse(await editor.SearchAsync());
        Assert.AreEqual(0, calls);
        Assert.AreEqual(
            "검색할 작품명을 입력하세요.",
            editor.ErrorMessage);
    }

    [TestMethod]
    public async Task SearchAsync_WhileRunningBlocksDuplicateSearchAndSave()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<MetadataCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var searches = 0;
        var commits = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "Movie" },
            null,
            (_, _) =>
            {
                commits++;
                return Task.FromResult<string?>(null);
            },
            async (_, _) =>
            {
                searches++;
                started.TrySetResult();
                return await release.Task;
            },
            (_, _) => throw new AssertFailedException(
                "상세 조회를 호출하면 안 됩니다."));

        var first = editor.SearchAsync();
        await started.Task;

        Assert.IsTrue(editor.IsLookupInProgress);
        Assert.IsFalse(editor.IsNotBusy);
        Assert.IsFalse(await editor.SearchAsync());
        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(1, searches);
        Assert.AreEqual(0, commits);

        release.TrySetResult([]);
        Assert.IsTrue(await first);
    }

    [TestMethod]
    public async Task DerivedState_RaisesPropertyChangedWhenDependenciesChange()
    {
        var searchStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var searchRelease =
            new TaskCompletionSource<IReadOnlyList<MetadataCandidate>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var saveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var saveRelease = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tv = new MetadataCandidate(
            "test",
            "tv-series",
            "1",
            MediaType.TvEpisode);
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord { Title = "시리즈" },
            null,
            async (_, _) =>
            {
                saveStarted.TrySetResult();
                return await saveRelease.Task;
            },
            async (_, _) =>
            {
                searchStarted.TrySetResult();
                return await searchRelease.Task;
            },
            (_, _) => throw new AssertFailedException(
                "회차 확정 전 상세 조회를 호출하면 안 됩니다."));
        var changed = new List<string?>();
        editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.IsFalse(await editor.SelectCandidateAsync(tv));
        CollectionAssert.Contains(
            changed,
            nameof(MetadataEditorViewModel.CanApplyTvEpisode));

        changed.Clear();
        editor.SeasonNumberText = "2";
        CollectionAssert.Contains(
            changed,
            nameof(MetadataEditorViewModel.CanApplyTvEpisode));

        changed.Clear();
        editor.EpisodeNumberText = "3";
        CollectionAssert.Contains(
            changed,
            nameof(MetadataEditorViewModel.CanApplyTvEpisode));
        Assert.IsTrue(editor.CanApplyTvEpisode);

        changed.Clear();
        var search = editor.SearchAsync();
        await searchStarted.Task;
        CollectionAssert.Contains(
            changed,
            nameof(MetadataEditorViewModel.IsNotBusy));
        searchRelease.TrySetResult([]);
        Assert.IsTrue(await search);

        changed.Clear();
        var save = editor.SaveAsync();
        await saveStarted.Task;
        CollectionAssert.Contains(
            changed,
            nameof(MetadataEditorViewModel.IsNotBusy));
        saveRelease.TrySetResult(null);
        Assert.IsTrue(await save);
    }

    [TestMethod]
    public async Task ApplyTvEpisodeAsync_WhileRunningBlocksAllCompetingOperations()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MetadataDetails>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tv = new MetadataCandidate(
            "test",
            "tv-series",
            "1",
            MediaType.TvEpisode);
        var other = new MetadataCandidate(
            "test",
            "movie",
            "2",
            MediaType.Movie);
        var searches = 0;
        var detailsCalls = 0;
        var commits = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord { Title = "기존 제목" },
            null,
            (_, _) =>
            {
                commits++;
                return Task.FromResult<string?>(null);
            },
            (_, _) =>
            {
                searches++;
                return Task.FromResult<IReadOnlyList<MetadataCandidate>>([]);
            },
            (_, _) =>
            {
                detailsCalls++;
                started.TrySetResult();
                return release.Task;
            });
        Assert.IsFalse(await editor.SelectCandidateAsync(tv));
        editor.SeasonNumberText = "2";
        editor.EpisodeNumberText = "3";

        var applying = editor.ApplyTvEpisodeAsync();
        await started.Task;

        Assert.IsFalse(await editor.ApplyTvEpisodeAsync());
        Assert.IsFalse(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(other));
        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(0, searches);
        Assert.AreEqual(1, detailsCalls);
        Assert.AreEqual(0, commits);
        Assert.AreSame(tv, editor.PendingTvCandidate);
        Assert.AreEqual("2", editor.SeasonNumberText);
        Assert.AreEqual("3", editor.EpisodeNumberText);
        Assert.AreEqual("기존 제목", editor.Title);
        Assert.IsFalse(editor.HasSelectedResult);

        release.TrySetResult(new(
            MediaType: MediaType.TvEpisode,
            Title: null,
            OriginalTitle: "Series",
            SeriesTitle: "시리즈",
            EpisodeTitle: "회차",
            ReleaseDate: new DateOnly(2024, 1, 2),
            Genres: ["드라마"],
            Director: "감독",
            Actors: ["배우"],
            Synopsis: "줄거리",
            SeasonNumber: 2,
            EpisodeNumber: 3,
            PosterUri: null,
            ProviderReferences:
            [
                new("test", "tv-series", "1"),
                new("test", "tv-episode", "20")
            ]));

        Assert.IsTrue(await applying);
        Assert.AreEqual("시리즈 S02E03 · 회차", editor.Title);
    }

    [TestMethod]
    public async Task SearchAsync_NoResults_ClearsLookupStateAndPreservesAppliedDraft()
    {
        var movie = new MetadataCandidate(
            "test",
            "movie",
            "1",
            MediaType.Movie);
        var tv = new MetadataCandidate(
            "test",
            "tv-series",
            "2",
            MediaType.TvEpisode);
        var results = new Queue<IReadOnlyList<MetadataCandidate>>(
        [
            [tv],
            []
        ]);
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "기존 제목" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult(results.Dequeue()),
            (_, _) => Task.FromResult(new MetadataDetails(
                MediaType: MediaType.Movie,
                Title: "선택 제목",
                OriginalTitle: "Selected",
                SeriesTitle: null,
                EpisodeTitle: null,
                ReleaseDate: new DateOnly(2024, 1, 2),
                Genres: ["드라마"],
                Director: "감독",
                Actors: ["배우"],
                Synopsis: "줄거리",
                SeasonNumber: null,
                EpisodeNumber: null,
                PosterUri: null,
                ProviderReferences: [new("test", "movie", "1")])));

        Assert.IsTrue(await editor.SelectCandidateAsync(movie));
        editor.Title = "사용자 제목";
        Assert.IsTrue(await editor.SearchAsync());
        Assert.AreSame(tv, editor.SearchCandidates.Single());
        Assert.IsFalse(await editor.SelectCandidateAsync(tv));
        editor.SeasonNumberText = "2";
        editor.EpisodeNumberText = "3";

        editor.SearchText = "없는 작품";
        Assert.IsTrue(await editor.SearchAsync());

        Assert.AreEqual(0, editor.SearchCandidates.Count);
        Assert.IsNull(editor.PendingTvCandidate);
        Assert.AreEqual("2", editor.SeasonNumberText);
        Assert.AreEqual("3", editor.EpisodeNumberText);
        Assert.IsTrue(editor.IsSearchPopupOpen);
        Assert.AreEqual("검색 결과가 없습니다", editor.ErrorMessage);
        Assert.AreEqual("사용자 제목", editor.Title);
        Assert.IsTrue(editor.HasSelectedResult);
    }

    [TestMethod]
    public async Task SearchAsync_WhenProviderFails_PreservesFormAndHidesSecret()
    {
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "기존 제목" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromException<IReadOnlyList<MetadataCandidate>>(
                new MetadataProviderException(
                    MetadataProviderFailureKind.Authentication,
                    "secret-token")),
            (_, _) => throw new AssertFailedException(
                "상세 조회를 호출하면 안 됩니다."));

        Assert.IsFalse(await editor.SearchAsync());
        Assert.AreEqual("기존 제목", editor.Title);
        StringAssert.Contains(editor.ErrorMessage, ".env");
        Assert.IsFalse(
            editor.ErrorMessage!.Contains(
                "secret-token",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancelLookup_CancelsPendingProviderCall()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "Movie" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return [];
            },
            (_, _) => throw new AssertFailedException(
                "상세 조회를 호출하면 안 됩니다."));

        var search = editor.SearchAsync();
        await started.Task;
        editor.CancelLookup();

        Assert.IsFalse(await search);
        Assert.IsFalse(editor.IsLookupInProgress);
    }

    [TestMethod]
    public async Task SelectCandidateAsync_AppliesMovieOnlyAfterDetailsSucceed()
    {
        var release = new TaskCompletionSource<MetadataDetails>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = new MetadataCandidate(
            "test",
            "movie",
            "1",
            MediaType.Movie,
            DisplayTitle: "후보");
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "기존 제목" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
            (_, _) => release.Task);
        var formProperties = new[]
        {
            nameof(MetadataEditorViewModel.Title),
            nameof(MetadataEditorViewModel.OriginalTitle),
            nameof(MetadataEditorViewModel.ReleaseDate),
            nameof(MetadataEditorViewModel.Director),
            nameof(MetadataEditorViewModel.ActorsText),
            nameof(MetadataEditorViewModel.Synopsis)
        };
        var changed = new List<string?>();
        editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        editor.IsSearchPopupOpen = true;
        changed.Clear();
        var applying = editor.SelectCandidateAsync(candidate);
        Assert.AreEqual("기존 제목", editor.Title);
        Assert.IsFalse(changed.Any(name => formProperties.Contains(name)));

        release.TrySetResult(new(
            MediaType: MediaType.Movie,
            Title: "선택 제목",
            OriginalTitle: "Selected",
            SeriesTitle: null,
            EpisodeTitle: null,
            ReleaseDate: new DateOnly(2024, 1, 2),
            Genres: ["드라마"],
            Director: "감독",
            Actors: ["배우"],
            Synopsis: "줄거리",
            SeasonNumber: null,
            EpisodeNumber: null,
            PosterUri: new Uri("https://image.tmdb.org/poster.jpg"),
            ProviderReferences: [new("test", "movie", "1")]));

        Assert.IsTrue(await applying);
        foreach (var property in formProperties)
        {
            CollectionAssert.Contains(changed, property);
        }
        Assert.AreEqual("선택 제목", editor.Title);
        Assert.AreEqual("Selected", editor.OriginalTitle);
        Assert.AreEqual("감독", editor.Director);
        Assert.AreEqual("배우", editor.ActorsText);
        Assert.IsTrue(editor.HasSelectedResult);
        Assert.IsFalse(editor.IsSearchPopupOpen);
        Assert.AreEqual(
            "https://image.tmdb.org/poster.jpg",
            editor.SelectedPosterUri!.AbsoluteUri);
    }

    [TestMethod]
    public async Task SelectCandidateAsync_WhenDetailsFail_PreservesFormPopupAndPosterIntent()
    {
        var root = Directory.CreateTempSubdirectory("dabom-detail-failure-");
        try
        {
            var localPoster = Path.Combine(root.FullName, "local.png");
            WritePng(localPoster);
            var first = new MetadataCandidate(
                "test", "movie", "1", MediaType.Movie);
            var failing = new MetadataCandidate(
                "test", "movie", "2", MediaType.Movie);
            var priorPoster = new Uri("https://image.tmdb.org/prior.jpg");
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv",
                new VideoRecord { Title = "기존 제목" },
                null,
                (_, _) => Task.FromResult<string?>(null),
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
                (candidate, _) => candidate.ResourceId == "1"
                    ? Task.FromResult(new MetadataDetails(
                        MediaType: MediaType.Movie,
                        Title: "첫 기준선",
                        OriginalTitle: "First",
                        SeriesTitle: null,
                        EpisodeTitle: null,
                        ReleaseDate: new DateOnly(2024, 1, 2),
                        Genres: ["드라마"],
                        Director: "감독",
                        Actors: ["배우"],
                        Synopsis: "줄거리",
                        SeasonNumber: null,
                        EpisodeNumber: null,
                        PosterUri: priorPoster,
                        ProviderReferences: [new("test", "movie", "1")]))
                    : Task.FromException<MetadataDetails>(
                        new MetadataProviderException(
                            MetadataProviderFailureKind.Transient,
                            "secret-token")));
            Assert.IsTrue(await editor.SelectCandidateAsync(first));
            editor.Title = "사용자 제목";
            editor.ChoosePoster(localPoster);
            var preview = editor.PreviewPoster;
            editor.IsSearchPopupOpen = true;
            var formProperties = new[]
            {
                nameof(MetadataEditorViewModel.Title),
                nameof(MetadataEditorViewModel.OriginalTitle),
                nameof(MetadataEditorViewModel.ReleaseDate),
                nameof(MetadataEditorViewModel.Director),
                nameof(MetadataEditorViewModel.ActorsText),
                nameof(MetadataEditorViewModel.Synopsis)
            };
            var changed = new List<string?>();
            editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

            Assert.IsFalse(await editor.SelectCandidateAsync(failing));

            Assert.IsFalse(changed.Any(name => formProperties.Contains(name)));
            Assert.AreEqual("사용자 제목", editor.Title);
            Assert.IsTrue(editor.IsSearchPopupOpen);
            Assert.IsTrue(editor.HasSelectedResult);
            Assert.AreEqual(priorPoster, editor.SelectedPosterUri);
            Assert.AreEqual(localPoster, editor.SelectedPosterSourcePath);
            Assert.IsFalse(editor.RemovePoster);
            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.AreEqual(
                "온라인 메타데이터 조회에 실패했습니다. 잠시 후 다시 시도하세요.",
                editor.ErrorMessage);

            editor.MarkPosterRemoved();
            editor.IsSearchPopupOpen = true;
            Assert.IsFalse(await editor.SelectCandidateAsync(failing));
            Assert.IsTrue(editor.RemovePoster);
            Assert.IsNull(editor.SelectedPosterSourcePath);
            Assert.IsNull(editor.PreviewPoster);
            Assert.IsTrue(editor.IsSearchPopupOpen);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SelectCandidateAsync_SuccessClearsPendingPosterChoice()
    {
        var root = Directory.CreateTempSubdirectory("dabom-detail-poster-");
        try
        {
            var localPoster = Path.Combine(root.FullName, "local.png");
            WritePng(localPoster);
            var first = new MetadataCandidate(
                "test", "movie", "1", MediaType.Movie);
            var second = new MetadataCandidate(
                "test", "movie", "2", MediaType.Movie);
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv",
                new VideoRecord(),
                null,
                (_, _) => Task.FromResult<string?>(null),
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
                (candidate, _) => Task.FromResult(new MetadataDetails(
                    MediaType: MediaType.Movie,
                    Title: $"후보 {candidate.ResourceId}",
                    OriginalTitle: null,
                    SeriesTitle: null,
                    EpisodeTitle: null,
                    ReleaseDate: null,
                    Genres: [],
                    Director: null,
                    Actors: [],
                    Synopsis: null,
                    SeasonNumber: null,
                    EpisodeNumber: null,
                    PosterUri: new Uri($"https://image.tmdb.org/{candidate.ResourceId}.jpg"),
                    ProviderReferences:
                    [
                        new("test", "movie", candidate.ResourceId)
                    ])));

            editor.ChoosePoster(localPoster);
            Assert.IsTrue(await editor.SelectCandidateAsync(first));
            Assert.IsNull(editor.SelectedPosterSourcePath);
            Assert.IsFalse(editor.RemovePoster);

            editor.MarkPosterRemoved();
            Assert.IsTrue(editor.RemovePoster);
            Assert.IsTrue(await editor.SelectCandidateAsync(second));
            Assert.IsNull(editor.SelectedPosterSourcePath);
            Assert.IsFalse(editor.RemovePoster);
            Assert.AreEqual(
                "https://image.tmdb.org/2.jpg",
                editor.SelectedPosterUri!.AbsoluteUri);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SelectCandidateAsync_TvRequiresValidEditableEpisodeNumbers()
    {
        MetadataCandidate? requested = null;
        var candidate = new MetadataCandidate(
            "test",
            "tv-series",
            "10",
            MediaType.TvEpisode,
            DisplayTitle: "시리즈");
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                Title = "기존",
                SeasonNumber = 99,
                EpisodeNumber = 88
            },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
            (value, _) =>
            {
                requested = value;
                return Task.FromResult(new MetadataDetails(
                    MediaType: MediaType.TvEpisode,
                    Title: null,
                    OriginalTitle: "Series",
                    SeriesTitle: "시리즈",
                    EpisodeTitle: "회차",
                    ReleaseDate: new DateOnly(2024, 1, 2),
                    Genres: ["드라마"],
                    Director: "감독",
                    Actors: ["배우"],
                    Synopsis: "줄거리",
                    SeasonNumber: value.SeasonNumber,
                    EpisodeNumber: value.EpisodeNumber,
                    PosterUri: null,
                    ProviderReferences:
                    [
                        new("test", "tv-series", "10"),
                        new("test", "tv-episode", "20")
                    ]));
            });

        editor.IsSearchPopupOpen = true;
        Assert.IsFalse(await editor.SelectCandidateAsync(candidate));
        Assert.AreSame(candidate, editor.PendingTvCandidate);
        Assert.AreEqual("99", editor.SeasonNumberText);
        Assert.AreEqual("88", editor.EpisodeNumberText);

        editor.Title = "사용자 입력 유지";
        var hadSelectedResult = editor.HasSelectedResult;
        editor.SeasonNumberText = "-1";
        editor.EpisodeNumberText = "3";
        Assert.IsFalse(editor.CanApplyTvEpisode);
        Assert.IsFalse(await editor.ApplyTvEpisodeAsync());
        Assert.IsNull(requested);
        Assert.AreEqual(
            "시즌 번호에는 0 이상, 에피소드 번호에는 1 이상의 정수를 입력하세요.",
            editor.ErrorMessage);
        Assert.IsTrue(editor.IsSearchPopupOpen);
        Assert.AreSame(candidate, editor.PendingTvCandidate);
        Assert.AreEqual("-1", editor.SeasonNumberText);
        Assert.AreEqual("3", editor.EpisodeNumberText);
        Assert.AreEqual("사용자 입력 유지", editor.Title);
        Assert.AreEqual(hadSelectedResult, editor.HasSelectedResult);

        editor.SeasonNumberText = "2";
        Assert.IsTrue(editor.CanApplyTvEpisode);
        Assert.IsTrue(await editor.ApplyTvEpisodeAsync());
        Assert.AreEqual(2, requested!.SeasonNumber);
        Assert.AreEqual(3, requested.EpisodeNumber);
        Assert.AreEqual("시리즈 S02E03 · 회차", editor.Title);
    }

    [TestMethod]
    public async Task SelectCandidateAsync_TvKeepsInvalidExistingNumbersVisible()
    {
        var candidate = new MetadataCandidate(
            "test",
            "tv-series",
            "10",
            MediaType.TvEpisode,
            DisplayTitle: "시리즈");
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                SeasonNumber = 0,
                EpisodeNumber = -1
            },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
            (_, _) => throw new AssertFailedException(
                "회차 확정 전 상세 조회를 호출하면 안 됩니다."));

        Assert.IsFalse(await editor.SelectCandidateAsync(candidate));
        Assert.AreEqual("0", editor.SeasonNumberText);
        Assert.AreEqual("-1", editor.EpisodeNumberText);
        Assert.IsFalse(editor.CanApplyTvEpisode);
    }

    [TestMethod]
    public async Task ApplyTvEpisodeAsync_WhenDetailsFail_PreservesStateForRetry()
    {
        var movie = new MetadataCandidate(
            "test",
            "movie",
            "1",
            MediaType.Movie);
        var tv = new MetadataCandidate(
            "test",
            "tv-series",
            "2",
            MediaType.TvEpisode);
        var moviePoster = new Uri("https://image.tmdb.org/movie.jpg");
        var tvPoster = new Uri("https://image.tmdb.org/tv.jpg");
        var tvCalls = 0;
        MetadataCandidate? requested = null;
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord { Title = "기존 제목" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([tv]),
            (candidate, _) =>
            {
                if (candidate.MediaType == MediaType.Movie)
                {
                    return Task.FromResult(new MetadataDetails(
                        MediaType: MediaType.Movie,
                        Title: "영화 기준선",
                        OriginalTitle: "Movie",
                        SeriesTitle: null,
                        EpisodeTitle: null,
                        ReleaseDate: new DateOnly(2024, 1, 2),
                        Genres: ["드라마"],
                        Director: "감독",
                        Actors: ["배우"],
                        Synopsis: "줄거리",
                        SeasonNumber: null,
                        EpisodeNumber: null,
                        PosterUri: moviePoster,
                        ProviderReferences: [new("test", "movie", "1")]));
                }

                requested = candidate;
                tvCalls++;
                if (tvCalls == 1)
                {
                    return Task.FromException<MetadataDetails>(
                        new MetadataProviderException(
                            MetadataProviderFailureKind.Transient,
                            "secret-token"));
                }
                return Task.FromResult(new MetadataDetails(
                    MediaType: MediaType.TvEpisode,
                    Title: null,
                    OriginalTitle: "Series",
                    SeriesTitle: "시리즈",
                    EpisodeTitle: "회차",
                    ReleaseDate: new DateOnly(2024, 2, 3),
                    Genres: ["드라마"],
                    Director: "감독",
                    Actors: ["배우"],
                    Synopsis: "회차 줄거리",
                    SeasonNumber: candidate.SeasonNumber,
                    EpisodeNumber: candidate.EpisodeNumber,
                    PosterUri: tvPoster,
                    ProviderReferences:
                    [
                        new("test", "tv-series", "2"),
                        new("test", "tv-episode", "20")
                    ]));
            });

        Assert.IsTrue(await editor.SelectCandidateAsync(movie));
        editor.Title = "사용자 제목";
        editor.IsSearchPopupOpen = true;
        Assert.IsFalse(await editor.SelectCandidateAsync(tv));
        editor.SeasonNumberText = "2";
        editor.EpisodeNumberText = "3";

        Assert.IsFalse(await editor.ApplyTvEpisodeAsync());

        Assert.AreEqual(1, tvCalls);
        Assert.AreEqual(2, requested!.SeasonNumber);
        Assert.AreEqual(3, requested.EpisodeNumber);
        Assert.AreEqual(
            "온라인 메타데이터 조회에 실패했습니다. 잠시 후 다시 시도하세요.",
            editor.ErrorMessage);
        Assert.IsFalse(
            editor.ErrorMessage!.Contains(
                "secret-token",
                StringComparison.Ordinal));
        Assert.IsTrue(editor.IsSearchPopupOpen);
        Assert.AreSame(tv, editor.PendingTvCandidate);
        Assert.AreEqual("2", editor.SeasonNumberText);
        Assert.AreEqual("3", editor.EpisodeNumberText);
        Assert.AreEqual("사용자 제목", editor.Title);
        Assert.IsTrue(editor.HasSelectedResult);
        Assert.AreEqual(moviePoster, editor.SelectedPosterUri);

        Assert.IsTrue(await editor.ApplyTvEpisodeAsync());

        Assert.AreEqual(2, tvCalls);
        Assert.AreEqual(2, requested!.SeasonNumber);
        Assert.AreEqual(3, requested.EpisodeNumber);
        Assert.IsFalse(editor.IsSearchPopupOpen);
        Assert.IsNull(editor.PendingTvCandidate);
        Assert.AreEqual("시리즈 S02E03 · 회차", editor.Title);
        Assert.AreEqual(tvPoster, editor.SelectedPosterUri);
    }

    [TestMethod]
    public async Task BuildRecord_SelectedMovieTracksOnlyLaterFieldEdits()
    {
        var candidate = new MetadataCandidate(
            "test", "movie", "1", MediaType.Movie);
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord
            {
                Title = "원본",
                Genres = ["보호 장르"],
                Poster = "posters/old.png",
                UserEditedFields =
                [
                    MetadataField.Title,
                    MetadataField.Genres,
                    MetadataField.Poster
                ]
            },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
            (_, _) => Task.FromResult(MovieDetails("선택 후보", "1")));

        Assert.IsTrue(await editor.SelectCandidateAsync(candidate));
        editor.Synopsis = "사용자 줄거리";

        var record = editor.BuildRecord(null);

        CollectionAssert.AreEquivalent(
            new[] { MetadataField.Synopsis },
            record.UserEditedFields.ToArray());
        Assert.AreEqual(MetadataStatus.Matched, record.MetadataStatus);
        Assert.AreEqual("1", record.ProviderReferences.Single().ResourceId);
        CollectionAssert.AreEqual(new[] { "드라마" }, record.Genres);
    }

    [TestMethod]
    public async Task BuildRecord_SecondCandidateResetsComparisonBaseline()
    {
        var first = new MetadataCandidate(
            "test", "movie", "1", MediaType.Movie);
        var second = new MetadataCandidate(
            "test", "movie", "2", MediaType.Movie);
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord { Title = "원본" },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
            (candidate, _) => Task.FromResult(
                MovieDetails(
                    candidate.ResourceId == "1"
                        ? "첫 번째 후보"
                        : "두 번째 후보",
                    candidate.ResourceId)));

        Assert.IsTrue(await editor.SelectCandidateAsync(first));
        editor.Title = "사용자 제목";
        Assert.IsTrue(await editor.SelectCandidateAsync(second));

        var record = editor.BuildRecord(null);

        Assert.AreEqual("두 번째 후보", record.Title);
        Assert.AreEqual(0, record.UserEditedFields.Count);
        Assert.AreEqual(
            "2",
            record.ProviderReferences.Single().ResourceId);
    }

    [TestMethod]
    public async Task BuildRecord_SelectedPosterTracksOnlyExplicitLocalEdit()
    {
        var root = Directory.CreateTempSubdirectory("dabom-record-poster-");
        try
        {
            var localPosterPath = Path.Combine(root.FullName, "local.png");
            WritePng(localPosterPath);
            var candidate = new MetadataCandidate(
                "test", "movie", "1", MediaType.Movie);
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv",
                new VideoRecord(),
                null,
                (_, _) => Task.FromResult<string?>(null),
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
                (_, _) => Task.FromResult(MovieDetails("선택 후보", "1")));
            Assert.IsTrue(await editor.SelectCandidateAsync(candidate));

            var remoteDefault = editor.BuildRecord("posters/remote.jpg");
            Assert.IsFalse(
                remoteDefault.UserEditedFields.Contains(MetadataField.Poster));

            editor.MarkPosterRemoved();
            var removed = editor.BuildRecord(null);
            Assert.IsTrue(
                removed.UserEditedFields.Contains(MetadataField.Poster));

            editor.ChoosePoster(localPosterPath);
            var chosen = editor.BuildRecord("posters/local.png");
            Assert.IsTrue(
                chosen.UserEditedFields.Contains(MetadataField.Poster));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void TvDraft_InitializesStructuredFieldsAndRegeneratesOnlyWhenValid()
    {
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                Title = "직접 제목",
                MediaType = MediaType.TvEpisode,
                SeriesTitle = "시리즈",
                SeasonNumber = 2,
                EpisodeTitle = "회차",
                EpisodeNumber = 3
            },
            null,
            (_, _) => Task.FromResult<string?>(null));

        Assert.IsTrue(editor.IsTvEpisode);
        Assert.AreEqual("시리즈", editor.SeriesTitle);
        Assert.AreEqual("2", editor.SeasonNumberText);
        Assert.AreEqual("회차", editor.EpisodeTitle);
        Assert.AreEqual("3", editor.EpisodeNumberText);

        editor.SeasonNumberText = "0";
        Assert.AreEqual("시리즈 S00E03 · 회차", editor.Title);
        editor.SeriesTitle = "새 시리즈";
        Assert.AreEqual("새 시리즈 S00E03 · 회차", editor.Title);
        editor.SeasonNumberText = "4";
        Assert.AreEqual("새 시리즈 S04E03 · 회차", editor.Title);

        editor.Title = "사용자 덮어쓰기";
        Assert.AreEqual("사용자 덮어쓰기", editor.Title);
        editor.EpisodeTitle = string.Empty;
        Assert.AreEqual("새 시리즈 S04E03", editor.Title);
    }

    [TestMethod]
    public async Task TvDraft_IncompleteExistingValuesStayVisibleButCannotSave()
    {
        var calls = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Incomplete.mkv",
            new VideoRecord
            {
                Title = "현재 제목",
                MediaType = MediaType.TvEpisode,
                SeriesTitle = "  ",
                SeasonNumber = 0,
                EpisodeTitle = null,
                EpisodeNumber = -1
            },
            null,
            (_, _) =>
            {
                calls++;
                return Task.FromResult<string?>(null);
            });

        Assert.AreEqual("  ", editor.SeriesTitle);
        Assert.AreEqual("0", editor.SeasonNumberText);
        Assert.AreEqual(string.Empty, editor.EpisodeTitle);
        Assert.AreEqual("-1", editor.EpisodeNumberText);
        Assert.AreEqual("현재 제목", editor.Title);
        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task OnlineLookup_PreservesTvDraftUntilSuccessfulDetailsReplaceBaseline()
    {
        var tv = new MetadataCandidate(
            "test", "tv-series", "10", MediaType.TvEpisode);
        var movie = new MetadataCandidate(
            "test", "movie", "20", MediaType.Movie);
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                Title = "기존",
                MediaType = MediaType.TvEpisode,
                SeriesTitle = "편집 시리즈",
                SeasonNumber = 7,
                EpisodeTitle = "편집 회차",
                EpisodeNumber = 8
            },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([tv, movie]),
            (candidate, _) => Task.FromResult(candidate.MediaType == MediaType.Movie
                ? MovieDetails("영화", "20")
                : throw new AssertFailedException("TV 회차 적용 전 상세 조회 금지")));

        editor.SeriesTitle = "사용자 시리즈";
        editor.SeasonNumberText = "9";
        editor.EpisodeTitle = "사용자 회차";
        editor.EpisodeNumberText = "10";
        Assert.IsTrue(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(tv));

        Assert.AreEqual("사용자 시리즈", editor.SeriesTitle);
        Assert.AreEqual("9", editor.SeasonNumberText);
        Assert.AreEqual("사용자 회차", editor.EpisodeTitle);
        Assert.AreEqual("10", editor.EpisodeNumberText);

        Assert.IsTrue(await editor.SelectCandidateAsync(movie));
        Assert.IsFalse(editor.IsTvEpisode);
        Assert.AreEqual(string.Empty, editor.SeriesTitle);
        Assert.AreEqual(string.Empty, editor.SeasonNumberText);
        Assert.AreEqual(string.Empty, editor.EpisodeTitle);
        Assert.AreEqual(string.Empty, editor.EpisodeNumberText);
        Assert.AreEqual(MediaType.Movie, editor.BuildRecord(null).MediaType);
    }

    [TestMethod]
    public async Task OnlineLookup_InvalidTvDetailsPreserveDraftAndShowValidation()
    {
        var candidate = new MetadataCandidate(
            "test", "tv-series", "10", MediaType.TvEpisode);
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                Title = "기존 제목",
                MediaType = MediaType.TvEpisode,
                SeriesTitle = "사용자 시리즈",
                SeasonNumber = 2,
                EpisodeTitle = "사용자 회차",
                EpisodeNumber = 3
            },
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([candidate]),
            (_, _) => Task.FromResult(new MetadataDetails(
                MediaType: MediaType.TvEpisode,
                Title: null,
                OriginalTitle: null,
                SeriesTitle: " ",
                EpisodeTitle: "조회 회차",
                ReleaseDate: null,
                Genres: [],
                Director: null,
                Actors: [],
                Synopsis: null,
                SeasonNumber: 2,
                EpisodeNumber: 3,
                PosterUri: null,
                ProviderReferences: [])));

        Assert.IsFalse(await editor.SelectCandidateAsync(candidate));
        editor.SeasonNumberText = "2";
        editor.EpisodeNumberText = "3";

        Assert.IsFalse(await editor.ApplyTvEpisodeAsync());
        Assert.AreEqual("사용자 시리즈", editor.SeriesTitle);
        Assert.AreEqual("사용자 회차", editor.EpisodeTitle);
        Assert.AreEqual("기존 제목", editor.Title);
        Assert.IsNotNull(editor.TvValidationMessage);
        Assert.IsNotNull(editor.ErrorMessage);
        Assert.IsFalse(editor.HasSelectedResult);
    }

    [TestMethod]
    public async Task TvSave_ValidatesBeforeCommitAndTracksStructuredEdits()
    {
        VideoRecord? committed = null;
        var calls = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord
            {
                Title = "시리즈 S01E01",
                MediaType = MediaType.TvEpisode,
                SeriesTitle = "시리즈",
                SeasonNumber = 1,
                EpisodeNumber = 1
            },
            null,
            (value, _) =>
            {
                calls++;
                committed = value.BuildRecord(null);
                return Task.FromResult<string?>(null);
            });

        editor.SeriesTitle = "  ";
        editor.SeasonNumberText = "문자";
        editor.EpisodeNumberText = "0";

        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(0, calls);
        Assert.IsNotNull(editor.TvValidationMessage);
        Assert.AreEqual("시리즈 S01E01", editor.Title);

        editor.SeriesTitle = "새 시리즈";
        editor.SeasonNumberText = "2";
        editor.EpisodeTitle = "새 회차";
        editor.EpisodeNumberText = "3";
        Assert.IsTrue(await editor.SaveAsync());

        Assert.AreEqual(1, calls);
        Assert.AreEqual("새 시리즈", committed!.SeriesTitle);
        Assert.AreEqual(2, committed.SeasonNumber);
        Assert.AreEqual("새 회차", committed.EpisodeTitle);
        Assert.AreEqual(3, committed.EpisodeNumber);
        Assert.AreEqual("새 시리즈 S02E03 · 새 회차", committed.Title);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                MetadataField.Title,
                MetadataField.SeriesTitle,
                MetadataField.SeasonNumber,
                MetadataField.EpisodeTitle,
                MetadataField.EpisodeNumber
            },
            committed.UserEditedFields.ToArray());
    }

    [TestMethod]
    public async Task TvLookup_TitleOnlyBrowsesSeasonZeroAndEpisode()
    {
        var series = new MetadataCandidate(
            "test", "tv-series", "1431", MediaType.TvEpisode,
            DisplayTitle: "CSI");
        var editor = CreateTvLookupEditor(
            search: _ => [series],
            seasons: _ => [new(0, "Specials", 1)],
            episodes: (_, season) => season == 0
                ? [new(1, "특별편", new DateOnly(2001, 1, 1))]
                : []);
        editor.SearchText = "CSI";

        Assert.IsTrue(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(series));
        Assert.IsTrue(editor.IsSeasonStep);
        Assert.AreEqual(0, editor.TvSeasons.Single().SeasonNumber);
        Assert.IsFalse(await editor.SelectSeasonAsync(editor.TvSeasons.Single()));
        Assert.IsTrue(editor.IsEpisodeStep);
        Assert.IsTrue(await editor.SelectEpisodeAsync(editor.TvEpisodes.Single()));
        Assert.AreEqual("0", editor.SeasonNumberText);
        Assert.AreEqual("1", editor.EpisodeNumberText);
        Assert.IsTrue(await editor.SaveAsync());
    }

    [TestMethod]
    public async Task TvLookup_ExactSuffixSearchesTitleAndAutomaticallyAppliesEpisode()
    {
        string? searched = null;
        MetadataCandidate? requested = null;
        var series = new MetadataCandidate(
            "test", "tv-series", "1431", MediaType.TvEpisode,
            DisplayTitle: "CSI");
        var editor = CreateTvLookupEditor(
            search: title => { searched = title; return [series]; },
            seasons: _ => [new(1, "Season 1", 23)],
            episodes: (_, _) => [new(1, "Pilot", new DateOnly(2000, 10, 6))],
            onDetails: candidate => requested = candidate);
        editor.SearchText = "CSI: Crime Scene Investigation S01 E01";

        Assert.IsTrue(await editor.SearchAsync());
        Assert.AreEqual("CSI: Crime Scene Investigation", searched);
        Assert.AreEqual("조회할 회차: S01E01", editor.LookupHintText);
        Assert.IsTrue(await editor.SelectCandidateAsync(series));
        Assert.AreEqual(1, requested?.SeasonNumber);
        Assert.AreEqual(1, requested?.EpisodeNumber);
    }

    [TestMethod]
    public async Task TvLookup_MissingHintFallsBackToClosestSelectionStep()
    {
        var series = new MetadataCandidate(
            "test", "tv-series", "1431", MediaType.TvEpisode);
        var editor = CreateTvLookupEditor(
            search: _ => [series],
            seasons: _ => [new(0, "Specials", 1)],
            episodes: (_, _) => []);
        editor.SearchText = "CSI S01E01";

        Assert.IsTrue(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(series));
        Assert.IsTrue(editor.IsSeasonStep);
    }

    [TestMethod]
    public async Task TvLookup_MissingHintEpisodeFallsBackToEpisodeStep()
    {
        var series = new MetadataCandidate(
            "test", "tv-series", "1431", MediaType.TvEpisode);
        var editor = CreateTvLookupEditor(
            search: _ => [series],
            seasons: _ => [new(1, "Season 1", 1)],
            episodes: (_, _) => [new(2, "Second", new DateOnly(2000, 10, 13))]);
        editor.SearchText = "CSI S01E01";

        Assert.IsTrue(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(series));
        Assert.IsTrue(editor.IsEpisodeStep);
        Assert.AreEqual(2, editor.TvEpisodes.Single().EpisodeNumber);
    }

    [TestMethod]
    public async Task GoBackInLookup_WhileEpisodeLookupIsRunning_KeepsSeasonStep()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<TvEpisodeCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var series = new MetadataCandidate(
            "test", "tv-series", "1431", MediaType.TvEpisode);
        var editor = new MetadataEditorViewModel(
            @"D:\Episode.mkv",
            new VideoRecord(),
            null,
            (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([series]),
            (_, _) => throw new AssertFailedException(
                "에피소드 선택 전 상세 조회를 호출하면 안 됩니다."),
            (_, _) => Task.FromResult<IReadOnlyList<TvSeasonCandidate>>(
                [new(1, "Season 1", 1)]),
            async (_, _, _) =>
            {
                started.TrySetResult();
                return await release.Task;
            });

        editor.SearchText = "CSI";
        Assert.IsTrue(await editor.SearchAsync());
        Assert.IsFalse(await editor.SelectCandidateAsync(series));
        var selecting = editor.SelectSeasonAsync(editor.TvSeasons.Single());
        await started.Task;

        try
        {
            editor.GoBackInLookup();

            Assert.IsTrue(editor.IsSeasonStep);
            Assert.AreSame(series, editor.PendingTvCandidate);
        }
        finally
        {
            release.TrySetResult([new(1, "Pilot", new DateOnly(2000, 10, 6))]);
            await selecting;
        }
    }

    private static MetadataEditorViewModel CreateTvLookupEditor(
        Func<string, IReadOnlyList<MetadataCandidate>> search,
        Func<MetadataCandidate, IReadOnlyList<TvSeasonCandidate>> seasons,
        Func<MetadataCandidate, int, IReadOnlyList<TvEpisodeCandidate>> episodes,
        Action<MetadataCandidate>? onDetails = null) =>
        new(
            @"D:\Episode.mkv",
            new VideoRecord(),
            null,
            (_, _) => Task.FromResult<string?>(null),
            (title, _) => Task.FromResult(search(title)),
            (candidate, _) =>
            {
                onDetails?.Invoke(candidate);
                return Task.FromResult(new MetadataDetails(
                    MediaType: MediaType.TvEpisode,
                    Title: null,
                    OriginalTitle: "Series",
                    SeriesTitle: "시리즈",
                    EpisodeTitle: "회차",
                    ReleaseDate: new DateOnly(2001, 1, 1),
                    Genres: [],
                    Director: null,
                    Actors: [],
                    Synopsis: null,
                    SeasonNumber: candidate.SeasonNumber,
                    EpisodeNumber: candidate.EpisodeNumber,
                    PosterUri: null,
                    ProviderReferences:
                    [
                        new("test", "tv-series", "1431"),
                        new("test", "tv-episode", "100")
                    ]));
            },
            (series, _) => Task.FromResult(seasons(series)),
            (series, season, _) => Task.FromResult(episodes(series, season)));

    private static MetadataDetails MovieDetails(string title, string id) => new(
        MediaType: MediaType.Movie,
        Title: title,
        OriginalTitle: title,
        SeriesTitle: null,
        EpisodeTitle: null,
        ReleaseDate: new DateOnly(2024, 1, 2),
        Genres: ["드라마"],
        Director: "감독",
        Actors: ["배우"],
        Synopsis: "줄거리",
        SeasonNumber: null,
        EpisodeNumber: null,
        PosterUri: new Uri("https://image.tmdb.org/poster.jpg"),
        ProviderReferences: [new("test", "movie", id)]);

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
