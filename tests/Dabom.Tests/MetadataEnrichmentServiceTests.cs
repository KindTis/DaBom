using Dabom.Library;
using Dabom.Metadata;
using System.IO;
using System.Net;
using System.Net.Http;

namespace Dabom.Tests;

[TestClass]
public sealed class MetadataEnrichmentServiceTests
{
    private DirectoryInfo _root = null!;
    private HttpClient _imageClient = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Directory.CreateTempSubdirectory("dabom-enrichment-");
        _imageClient = new HttpClient(new ResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _imageClient.Dispose();
        _root.Delete(true);
    }

    [TestMethod]
    public async Task EnrichAsync_UsesFirstCandidateAndStopsAfterCompleteProvider()
    {
        var first = FakeProvider.WithMovie(
            candidates:
            [
                new("first", "movie", "1", MediaType.Movie),
                new("first", "movie", "2", MediaType.Movie)
            ],
            details: MovieDetails("첫 후보", "1", "first"));
        var second = FakeProvider.WithMovie(
            [new("second", "movie", "9", MediaType.Movie)],
            MovieDetails("두 번째 공급자", "9", "second"));
        var committed = new List<VideoRecord>();
        var progress = new List<MetadataProgress>();
        var service = CreateService(first, second);
        var records = Records(("Movie.2024.mkv", MetadataStatus.Pending));

        var summary = await service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, record, _, _) =>
            {
                committed.Add(record);
                return Task.CompletedTask;
            },
            progress.Add,
            CancellationToken.None);

        Assert.AreEqual(1, summary.Matched);
        Assert.AreEqual("첫 후보", committed.Single().Title);
        Assert.AreEqual(
            "1",
            committed.Single().ProviderReferences.Single().ResourceId);
        Assert.AreEqual(1, first.DetailsCalls);
        Assert.AreEqual(0, second.SearchCalls);
        Assert.AreEqual(1, progress.Single().Completed);
        Assert.AreEqual(1, progress.Single().Matched);
    }

    [TestMethod]
    public async Task EnrichAsync_FallsBackAndClassifiesNotFoundVersusFailed()
    {
        var empty = FakeProvider.Empty("empty");
        var failing = FakeProvider.Failing(
            "failing",
            new MetadataProviderException(
                MetadataProviderFailureKind.Transient,
                "provider unavailable"));

        var notFound = await RunSingleAsync([empty], "Unknown.Movie.mkv");
        var failed = await RunSingleAsync([empty, failing], "Broken.Movie.mkv");

        Assert.AreEqual(MetadataStatus.NotFound, notFound.Record.MetadataStatus);
        Assert.AreEqual(MetadataStatus.Failed, failed.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_SkipsTerminalStatesAndRetriesFailed()
    {
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            MovieDetails("재시도 성공", "1"));
        var committed = new List<string>();
        var records = Records(
            ("pending.mkv", MetadataStatus.Pending),
            ("failed.mkv", MetadataStatus.Failed),
            ("matched.mkv", MetadataStatus.Matched),
            ("notfound.mkv", MetadataStatus.NotFound),
            ("manual.mkv", MetadataStatus.Manual));

        await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (path, _, _, _) =>
            {
                committed.Add(path);
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.GetFullPath("pending.mkv"),
                Path.GetFullPath("failed.mkv")
            },
            committed);
        Assert.AreEqual(2, provider.SearchCalls);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenParserReturnsNull_DoesNotCallProviderAndCommitsNotFound()
    {
        var provider = FakeProvider.Empty("fake");

        var result = await RunSingleAsync([provider], "._-().mkv");

        Assert.AreEqual(MetadataStatus.NotFound, result.Record.MetadataStatus);
        Assert.AreEqual(0, provider.SearchCalls);
        Assert.AreEqual(1, result.Summary.NotFound);
    }

    [TestMethod]
    public async Task EnrichAsync_TvEpisodeBuildsStructuredTitleAndTwoReferences()
    {
        var details = TvDetails("시리즈", "에피소드", "fake");
        var provider = new FakeProvider(
            "fake",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new(
                    "fake",
                    "episode",
                    "episode-1",
                    MediaType.TvEpisode,
                    2,
                    3)
            ]),
            (_, _) => Task.FromResult(details));

        var result = await RunSingleAsync(
            [provider],
            "Series.S02E03.mkv");

        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
        Assert.AreEqual("시리즈 S02E03 · 에피소드", result.Record.Title);
        Assert.AreEqual(2, result.Record.ProviderReferences.Length);
    }

    [TestMethod]
    public async Task EnrichAsync_PreservesEveryUserEditedField()
    {
        var current = new VideoRecord
        {
            Title = "사용자 제목",
            OriginalTitle = "사용자 원제",
            SeriesTitle = "사용자 시리즈",
            EpisodeTitle = "사용자 회차",
            ReleaseDate = new DateOnly(2001, 2, 3),
            Genres = ["사용자 장르"],
            Director = "사용자 감독",
            Actors = ["사용자 배우"],
            Synopsis = "사용자 줄거리",
            Poster = "posters/user.png",
            MediaType = MediaType.Movie,
            SeasonNumber = 8,
            EpisodeNumber = 9,
            MetadataStatus = MetadataStatus.Failed,
            UserEditedFields = Enum.GetValues<MetadataField>().ToHashSet()
        };
        var provider = FakeProvider.WithMovie(
            [new("fake", "film", "1", MediaType.Movie)],
            MovieDetails("공급자 제목", "1"));

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            current);

        Assert.AreEqual(current.Title, result.Record.Title);
        Assert.AreEqual(current.OriginalTitle, result.Record.OriginalTitle);
        Assert.AreEqual(current.SeriesTitle, result.Record.SeriesTitle);
        Assert.AreEqual(current.EpisodeTitle, result.Record.EpisodeTitle);
        Assert.AreEqual(current.ReleaseDate, result.Record.ReleaseDate);
        CollectionAssert.AreEqual(current.Genres, result.Record.Genres);
        Assert.AreEqual(current.Director, result.Record.Director);
        CollectionAssert.AreEqual(current.Actors, result.Record.Actors);
        Assert.AreEqual(current.Synopsis, result.Record.Synopsis);
        Assert.AreEqual(current.Poster, result.Record.Poster);
        Assert.AreEqual(current.MediaType, result.Record.MediaType);
        Assert.AreEqual(current.SeasonNumber, result.Record.SeasonNumber);
        Assert.AreEqual(current.EpisodeNumber, result.Record.EpisodeNumber);
        CollectionAssert.AreEquivalent(
            current.UserEditedFields.ToArray(),
            result.Record.UserEditedFields.ToArray());
        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenSeriesTitleIsProtected_RebuildsTitleFromProtectedValue()
    {
        var current = new VideoRecord
        {
            Title = "이전 제목",
            SeriesTitle = "사용자 시리즈",
            MetadataStatus = MetadataStatus.Failed,
            UserEditedFields = [MetadataField.SeriesTitle]
        };
        var provider = new FakeProvider(
            "fake",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new(
                    "fake",
                    "episode",
                    "episode-1",
                    MediaType.TvEpisode,
                    2,
                    3)
            ]),
            (_, _) => Task.FromResult(TvDetails("공급자 시리즈", "회차", "fake")));

        var result = await RunSingleAsync(
            [provider],
            "Series.S02E03.mkv",
            current);

        Assert.AreEqual("사용자 시리즈", result.Record.SeriesTitle);
        Assert.AreEqual("사용자 시리즈 S02E03 · 회차", result.Record.Title);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenPosterIsProtected_DoesNotRequestPosterAndMatches()
    {
        var imageCalls = 0;
        using var imageClient = new HttpClient(new ResponseHandler(_ =>
        {
            imageCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var details = MovieDetails("제목", "1") with
        {
            PosterUri = new Uri("https://image.tmdb.org/poster.jpg"),
            PosterFailed = true
        };
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            details);
        var current = new VideoRecord
        {
            Title = "Movie",
            Poster = "posters/user.png",
            MetadataStatus = MetadataStatus.Failed,
            UserEditedFields = [MetadataField.Poster]
        };

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            current,
            imageClient);

        Assert.AreEqual(0, imageCalls);
        Assert.AreEqual("posters/user.png", result.Record.Poster);
        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenPosterDownloadFails_PreservesOldPosterAndCommitsTextAsFailed()
    {
        var details = MovieDetails("새 제목", "1") with
        {
            PosterUri = new Uri("https://image.tmdb.org/missing.jpg")
        };
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            details);
        var current = new VideoRecord
        {
            Title = "이전 제목",
            Poster = "posters/old.png",
            MetadataStatus = MetadataStatus.Failed
        };

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            current);

        Assert.AreEqual("새 제목", result.Record.Title);
        Assert.AreEqual("posters/old.png", result.Record.Poster);
        Assert.AreEqual(MetadataStatus.Failed, result.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenItemBudgetExpiresBeforeCompletion_CommitsFailedAndContinues()
    {
        var provider = new FakeProvider(
            "fake",
            async (query, token) =>
            {
                if (query.Title.Contains("Slow", StringComparison.Ordinal))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return
                [
                    new("fake", "movie", query.Title, MediaType.Movie)
                ];
            },
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId)));
        var records = Records(
            ("Slow.Movie.mkv", MetadataStatus.Pending),
            ("Fast.Movie.mkv", MetadataStatus.Pending));
        var committed = new List<VideoRecord>();
        var service = CreateTimedService(
            [provider],
            _imageClient,
            TimeSpan.FromMilliseconds(50));

        var summary = await service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, record, _, _) =>
            {
                committed.Add(record);
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        Assert.AreEqual(
            1,
            committed.Count(record =>
                record.MetadataStatus == MetadataStatus.Matched));
        Assert.AreEqual(
            1,
            committed.Count(record =>
                record.MetadataStatus == MetadataStatus.Failed));
        Assert.AreEqual(1, summary.Failed);
        Assert.AreEqual(1, summary.Matched);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenBudgetExpiresDuringOptionalRequestWithoutPoster_CommitsMatched()
    {
        var provider = ProviderReturningAfterCancellation(
            MovieDetails("부분 결과", "1"));

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            null,
            _imageClient,
            TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenBudgetExpiresDuringOptionalRequestWithRemotePoster_CommitsTextAsFailed()
    {
        var imageCalls = 0;
        using var imageClient = new HttpClient(new ResponseHandler(_ =>
        {
            imageCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var provider = ProviderReturningAfterCancellation(
            MovieDetails("부분 결과", "1") with
            {
                PosterUri = new Uri("https://image.tmdb.org/poster.jpg")
            });

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            null,
            imageClient,
            TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(0, imageCalls);
        Assert.AreEqual("부분 결과", result.Record.Title);
        Assert.AreEqual(MetadataStatus.Failed, result.Record.MetadataStatus);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenExternalCancellationOccursDuringOptionalRequest_Throws()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(
            "fake",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fake", "movie", "1", MediaType.Movie)
            ]),
            async (_, token) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                }
                return MovieDetails("부분 결과", "1");
            });
        var records = Records(("Movie.mkv", MetadataStatus.Pending));
        using var cancellation = new CancellationTokenSource();

        var enrich = CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            async () => await enrich);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenOptionalIssueOccursAfterCompletion_CommitsMatchedAndReportsAuth()
    {
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            MovieDetails("완성", "1") with
            {
                OptionalIssue = new(
                    MetadataProviderFailureKind.Authentication)
            });

        var result = await RunSingleAsync([provider], "Movie.mkv");

        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
        Assert.IsTrue(result.Summary.AuthenticationFailed);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenAuthenticationFailureOccurs_ContinuesWithNextVideo()
    {
        var provider = new FakeProvider(
            "fake",
            (query, _) => query.Title.Contains("First", StringComparison.Ordinal)
                ? Task.FromException<IReadOnlyList<MetadataCandidate>>(
                    new MetadataProviderException(
                        MetadataProviderFailureKind.Authentication,
                        "인증 실패"))
                : Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                [
                    new("fake", "movie", "2", MediaType.Movie)
                ]),
            (_, _) => Task.FromResult(MovieDetails("두 번째", "2")));
        var records = Records(
            ("First.Movie.mkv", MetadataStatus.Pending),
            ("Second.Movie.mkv", MetadataStatus.Pending));
        var committed = new List<VideoRecord>();

        var summary = await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, record, _, _) =>
            {
                committed.Add(record);
                return Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        Assert.AreEqual(
            1,
            committed.Count(record =>
                record.MetadataStatus == MetadataStatus.Matched));
        Assert.AreEqual(
            1,
            committed.Count(record =>
                record.MetadataStatus == MetadataStatus.Failed));
        Assert.AreEqual(2, provider.SearchCalls);
        Assert.IsTrue(summary.AuthenticationFailed);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenRetryAfterExceedsRemainingBudget_FallsBackWithoutWaiting()
    {
        var now = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var delayed = FakeProvider.Failing(
            "delayed",
            new MetadataProviderException(
                MetadataProviderFailureKind.Transient,
                "rate limited",
                TimeSpan.FromMinutes(1)));
        var fallback = new FakeProvider(
            "fallback",
            (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fallback", "movie", query.Title, MediaType.Movie)
            ]),
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId, "fallback")));
        var records = Records(("Movie.mkv", MetadataStatus.Pending));
        var service = new MetadataEnrichmentService(
            new MediaFilenameParser(),
            [delayed, fallback],
            new LibraryStore(_root.FullName),
            _imageClient,
            TimeSpan.FromSeconds(10),
            () => now,
            (_, _) => throw new AssertFailedException("기다리면 안 됩니다."));

        var summary = await service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            CancellationToken.None);

        Assert.AreEqual(1, summary.Matched);
        Assert.AreEqual(1, delayed.SearchCalls);
        Assert.AreEqual(1, fallback.SearchCalls);
    }

    [TestMethod]
    public async Task EnrichAsync_LimitsConcurrentCollectionToThreeAndAllowsOverlap()
    {
        var sync = new object();
        var active = 0;
        var peak = 0;
        var started = 0;
        var threeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(
            "fake",
            async (query, token) =>
            {
                lock (sync)
                {
                    active++;
                    peak = Math.Max(peak, active);
                    if (++started == 3) threeStarted.TrySetResult();
                }
                try
                {
                    await release.Task.WaitAsync(token);
                    return
                    [
                        new("fake", "movie", query.Title, MediaType.Movie)
                    ];
                }
                finally
                {
                    lock (sync) active--;
                }
            },
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId)));
        var records = Records(
            ("One.Movie.mkv", MetadataStatus.Pending),
            ("Two.Movie.mkv", MetadataStatus.Pending),
            ("Three.Movie.mkv", MetadataStatus.Pending),
            ("Four.Movie.mkv", MetadataStatus.Pending));

        var run = CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            CancellationToken.None);

        await threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(3, peak);
        Assert.AreEqual(3, provider.SearchCalls);
        release.TrySetResult();
        var summary = await run;

        Assert.AreEqual(4, summary.Matched);
        Assert.AreEqual(4, provider.SearchCalls);
        Assert.AreEqual(3, peak);
    }

    [TestMethod]
    public async Task EnrichAsync_SerializesCommitsAndReportsAfterEachAttempt()
    {
        var provider = new FakeProvider(
            "fake",
            (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fake", "movie", query.Title, MediaType.Movie)
            ]),
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId)));
        var records = Records(
            ("One.Movie.mkv", MetadataStatus.Pending),
            ("Two.Movie.mkv", MetadataStatus.Pending),
            ("Three.Movie.mkv", MetadataStatus.Pending),
            ("Four.Movie.mkv", MetadataStatus.Pending));
        var activeCommits = 0;
        var peakCommits = 0;
        var commitSync = new object();
        var attempts = 0;
        var finishedAttempts = 0;
        var progress = new List<MetadataProgress>();
        var progressAfterAttempts = new List<int>();

        var summary = await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            async (_, _, _, token) =>
            {
                var active = Interlocked.Increment(ref activeCommits);
                lock (commitSync)
                {
                    peakCommits = Math.Max(peakCommits, active);
                }
                var attempt = Interlocked.Increment(ref attempts);
                try
                {
                    await Task.Delay(20, token);
                    if (attempt == 2) throw new IOException("disk full");
                }
                finally
                {
                    Interlocked.Increment(ref finishedAttempts);
                    Interlocked.Decrement(ref activeCommits);
                }
            },
            value =>
            {
                progress.Add(value);
                progressAfterAttempts.Add(Volatile.Read(ref finishedAttempts));
            },
            CancellationToken.None);

        Assert.AreEqual(1, peakCommits);
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4 },
            progress.Select(value => value.Completed).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4 },
            progressAfterAttempts.ToArray());
        Assert.AreEqual(3, summary.Matched);
        Assert.AreEqual(1, summary.Failed);
        Assert.AreEqual(4, summary.Matched + summary.NotFound + summary.Failed);
    }

    [TestMethod]
    public async Task EnrichAsync_CancellationKeepsCommittedItemAndSkipsUnconfirmedProgress()
    {
        var records = Records(
            ("First.Movie.mkv", MetadataStatus.Pending),
            ("Second.Movie.mkv", MetadataStatus.Pending));
        var data = new LibraryData { VideosByPath = records };
        var store = new LibraryStore(_root.FullName);
        await store.SaveAsync(data);
        var firstCommitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(
            "fake",
            (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fake", "movie", query.Title, MediaType.Movie)
            ]),
            async (candidate, token) =>
            {
                if (candidate.ResourceId == "Second Movie")
                {
                    secondStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return MovieDetails(candidate.ResourceId, candidate.ResourceId);
            });
        var progress = new List<MetadataProgress>();
        using var cancellation = new CancellationTokenSource();

        var run = CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            async (path, record, createdPoster, token) =>
            {
                var nextRecords = new Dictionary<string, VideoRecord>(
                    data.VideosByPath,
                    StringComparer.OrdinalIgnoreCase)
                {
                    [path] = record
                };
                var next = data with { VideosByPath = nextRecords };
                await store.SaveAsync(next, createdPoster, token);
                data = next;
                firstCommitted.TrySetResult();
            },
            progress.Add,
            cancellation.Token);

        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await firstCommitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => run);

        var persisted = await new LibraryStore(_root.FullName)
            .LoadAsync(CancellationToken.None);
        Assert.AreEqual(
            MetadataStatus.Matched,
            persisted.VideosByPath[Path.GetFullPath("First.Movie.mkv")]
                .MetadataStatus);
        Assert.AreEqual(
            MetadataStatus.Pending,
            persisted.VideosByPath[Path.GetFullPath("Second.Movie.mkv")]
                .MetadataStatus);
        Assert.AreEqual(1, progress.Count);
        Assert.AreEqual(1, progress.Single().Completed);
    }

    [TestMethod]
    public async Task EnrichAsync_ConcurrentRetryAfterKeepsLatestAbsoluteExpiry()
    {
        var origin = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var nowTicks = origin.UtcTicks;
        var releaseRate = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var allRateStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var rateCalls = 0;
        var fallbackCalls = 0;
        var fallbackArrived = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var releaseFallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retries = new[]
        {
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2)
        };
        var rateLimited = new FakeProvider(
            "rate",
            async (_, token) =>
            {
                var callCount = Interlocked.Increment(ref rateCalls);
                if (callCount == 3) allRateStarted.TrySetResult();
                var call = callCount - 1;
                if (call >= releaseRate.Length)
                {
                    throw new AssertFailedException(
                        "가장 늦은 중단 시각 전에는 네 번째 요청을 시작하면 안 됩니다.");
                }
                await releaseRate[call].Task.WaitAsync(token);
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.Transient,
                    "rate limited",
                    retries[call]);
            },
            (_, _) => throw new AssertFailedException("상세 조회를 호출하면 안 됩니다."));
        var fallback = new FakeProvider(
            "fallback",
            async (query, token) =>
            {
                var call = Interlocked.Increment(ref fallbackCalls);
                if (call <= fallbackArrived.Length)
                    fallbackArrived[call - 1].TrySetResult();
                await releaseFallback.Task.WaitAsync(token);
                return
                [
                    new("fallback", "movie", query.Title, MediaType.Movie)
                ];
            },
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId, "fallback")));
        var records = Records(
            ("One.Movie.mkv", MetadataStatus.Pending),
            ("Two.Movie.mkv", MetadataStatus.Pending),
            ("Three.Movie.mkv", MetadataStatus.Pending),
            ("Four.Movie.mkv", MetadataStatus.Pending));
        var service = new MetadataEnrichmentService(
            new MediaFilenameParser(),
            [rateLimited, fallback],
            new LibraryStore(_root.FullName),
            _imageClient,
            TimeSpan.FromSeconds(10),
            () => new DateTimeOffset(Interlocked.Read(ref nowTicks), TimeSpan.Zero),
            (_, _) => throw new AssertFailedException("Retry-After를 기다리면 안 됩니다."));

        var run = service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            CancellationToken.None);

        await allRateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseRate[0].TrySetResult();
        await fallbackArrived[0].Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseRate[2].TrySetResult();
        await fallbackArrived[1].Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseRate[1].TrySetResult();
        await fallbackArrived[2].Task.WaitAsync(TimeSpan.FromSeconds(2));
        Interlocked.Exchange(
            ref nowTicks,
            origin.AddMinutes(3).UtcTicks);
        releaseFallback.TrySetResult();

        var summary = await run;
        Assert.AreEqual(3, rateCalls);
        Assert.AreEqual(4, fallbackCalls);
        Assert.AreEqual(4, summary.Matched);
    }

    [TestMethod]
    public async Task EnrichAsync_RetriesShortTransientFailuresAtMostThreeAttempts()
    {
        var calls = 0;
        var now = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var delays = new List<TimeSpan>();
        var provider = new FakeProvider(
            "fake",
            (_, _) =>
            {
                calls++;
                return calls < 3
                    ? Task.FromException<IReadOnlyList<MetadataCandidate>>(
                        new MetadataProviderException(
                            MetadataProviderFailureKind.Transient,
                            "temporary"))
                    : Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                    [
                        new("fake", "movie", "1", MediaType.Movie)
                    ]);
            },
            (_, _) => Task.FromResult(MovieDetails("성공", "1")));
        var service = new MetadataEnrichmentService(
            new MediaFilenameParser(),
            [provider],
            new LibraryStore(_root.FullName),
            _imageClient,
            TimeSpan.FromSeconds(10),
            () => now,
            (delay, _) =>
            {
                delays.Add(delay);
                now += delay;
                return Task.CompletedTask;
            });
        var records = Records(("Movie.mkv", MetadataStatus.Pending));

        var summary = await service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            CancellationToken.None);

        Assert.AreEqual(1, summary.Matched);
        Assert.AreEqual(3, provider.SearchCalls);
        CollectionAssert.AreEqual(
            new[]
            {
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500)
            },
            delays);
    }

    [TestMethod]
    public async Task EnrichAsync_AcceptsProviderOwnedOpaqueReferenceTypes()
    {
        var provider = new FakeProvider(
            "opaque",
            (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new(
                    "opaque",
                    query.MediaType == MediaType.Movie ? "film" : "episode",
                    "candidate",
                    query.MediaType,
                    query.SeasonNumber,
                    query.EpisodeNumber)
            ]),
            (candidate, _) => Task.FromResult(
                candidate.MediaType == MediaType.Movie
                    ? MovieDetails("영화", "1", "opaque") with
                    {
                        ProviderReferences =
                        [
                            new("opaque", "film", "1")
                        ]
                    }
                    : TvDetails("시리즈", "회차", "opaque") with
                    {
                        ProviderReferences =
                        [
                            new("opaque", "series", "1"),
                            new("opaque", "episode", "2")
                        ]
                    }));
        var records = Records(
            ("Movie.mkv", MetadataStatus.Pending),
            ("Series.S01E02.mkv", MetadataStatus.Pending));

        var summary = await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.CompletedTask,
            null,
            CancellationToken.None);

        Assert.AreEqual(2, summary.Matched);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenCandidateMediaTypeDiffers_FallsBackWithoutDetailsCall()
    {
        var invalid = FakeProvider.WithMovie(
            [new("invalid", "movie", "1", MediaType.Movie)],
            MovieDetails("잘못된 결과", "1", "invalid"));
        var valid = TvProvider("valid", TvDetails("시리즈", "회차", "valid"));

        var result = await RunSingleAsync(
            [invalid, valid],
            "Series.S01E02.mkv");

        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
        Assert.AreEqual(0, invalid.DetailsCalls);
        Assert.AreEqual(1, valid.DetailsCalls);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenDetailsMediaTypeDiffers_FallsBack()
    {
        var invalid = new FakeProvider(
            "invalid",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new(
                    "invalid",
                    "episode",
                    "1",
                    MediaType.TvEpisode,
                    1,
                    2)
            ]),
            (_, _) => Task.FromResult(
                MovieDetails("잘못된 결과", "1", "invalid")));
        var valid = TvProvider("valid", TvDetails("시리즈", "회차", "valid"));

        var result = await RunSingleAsync(
            [invalid, valid],
            "Series.S01E02.mkv");

        Assert.AreEqual(MetadataStatus.Matched, result.Record.MetadataStatus);
        Assert.AreEqual(1, invalid.DetailsCalls);
        Assert.AreEqual(1, valid.DetailsCalls);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenCommitFails_CountsFailureAndContinues()
    {
        var provider = new FakeProvider(
            "fake",
            (query, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fake", "movie", query.Title, MediaType.Movie)
            ]),
            (candidate, _) => Task.FromResult(
                MovieDetails(candidate.ResourceId, candidate.ResourceId)));
        var records = Records(
            ("First.Movie.mkv", MetadataStatus.Pending),
            ("Second.Movie.mkv", MetadataStatus.Pending));
        var attempts = 0;

        var summary = await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new IOException("disk full"))
                    : Task.CompletedTask;
            },
            null,
            CancellationToken.None);

        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, summary.Failed);
        Assert.AreEqual(1, summary.Matched);
    }

    [TestMethod]
    public async Task EnrichAsync_RequiredSuccessPathWithoutPosterCommitsFailed()
    {
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            MovieDetails("메타데이터 제목", "1"));

        var result = await RunSingleAsync(
            [provider],
            "Movie.mkv",
            requireSuccess: true);

        Assert.AreEqual(MetadataStatus.Failed, result.Record.MetadataStatus);
        Assert.AreEqual(MetadataStatus.Failed, result.Progress.Status);
        Assert.IsTrue(result.Progress.CommitSucceeded);
    }

    [TestMethod]
    public async Task EnrichAsync_WhenCommitFailsReportsCommitFailureOnce()
    {
        var provider = FakeProvider.WithMovie(
            [new("fake", "movie", "1", MediaType.Movie)],
            MovieDetails("메타데이터 제목", "1"));
        var records = Records(("Movie.mkv", MetadataStatus.Pending));
        var progress = new List<MetadataProgress>();

        var summary = await CreateService(provider).EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, _, _, _) => Task.FromException(new IOException("disk full")),
            progress.Add,
            CancellationToken.None);

        Assert.AreEqual(1, summary.Failed);
        Assert.AreEqual(1, progress.Count);
        Assert.AreEqual(MetadataStatus.Matched, progress.Single().Status);
        Assert.IsFalse(progress.Single().CommitSucceeded);
    }

    [TestMethod]
    public async Task EnrichAsync_RequiredSuccessPathsConvertEveryNonMatchToFailed()
    {
        var noResultProvider = FakeProvider.Empty("empty");
        var parseFailureProvider = FakeProvider.Empty("parse-failure");

        var noResult = await RunSingleAsync(
            [noResultProvider],
            "Unknown.Movie.mkv",
            requireSuccess: true);
        var parseFailure = await RunSingleAsync(
            [parseFailureProvider],
            "._-().mkv",
            requireSuccess: true);

        Assert.AreEqual(MetadataStatus.Failed, noResult.Record.MetadataStatus);
        Assert.AreEqual(0, noResult.Summary.NotFound);
        Assert.AreEqual(1, noResult.Summary.Failed);
        Assert.AreEqual(MetadataStatus.Failed, parseFailure.Record.MetadataStatus);
        Assert.AreEqual(0, parseFailure.Summary.NotFound);
        Assert.AreEqual(1, parseFailure.Summary.Failed);
        Assert.AreEqual(0, parseFailureProvider.SearchCalls);
    }

    [TestMethod]
    public async Task SearchManualAsync_UsesUnknownAndStopsAtFirstProviderWithResults()
    {
        MetadataQuery? received = null;
        var empty = FakeProvider.Empty("empty");
        var selected = new FakeProvider(
            "selected",
            (query, _) =>
            {
                received = query;
                return Task.FromResult<IReadOnlyList<MetadataCandidate>>(
                [
                    new(
                        "selected",
                        "movie",
                        "1",
                        MediaType.Movie,
                        DisplayTitle: "선택")
                ]);
            },
            (_, _) => Task.FromResult(MovieDetails("선택", "1", "selected")));
        var unused = FakeProvider.Empty("unused");

        var candidates = await CreateService(
            empty,
            selected,
            unused).SearchManualAsync("검색어", CancellationToken.None);

        Assert.AreEqual(MediaType.Unknown, received!.MediaType);
        Assert.AreEqual("검색어", received.Title);
        Assert.AreEqual(1, empty.SearchCalls);
        Assert.AreEqual(1, selected.SearchCalls);
        Assert.AreEqual(0, unused.SearchCalls);
        Assert.AreEqual("selected", candidates.Single().ProviderKey);
    }

    [TestMethod]
    public async Task SearchManualAsync_DistinguishesNoResultsFromProviderFailure()
    {
        var noResults = await CreateService(
            FakeProvider.Empty("first"),
            FakeProvider.Empty("second"))
            .SearchManualAsync("없음", CancellationToken.None);

        Assert.AreEqual(0, noResults.Count);

        var error = new MetadataProviderException(
            MetadataProviderFailureKind.Authentication,
            "secret value must not escape");
        var service = CreateService(
            FakeProvider.Empty("empty"),
            FakeProvider.Failing("failing", error));

        var thrown = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => service.SearchManualAsync("오류", CancellationToken.None));
        Assert.AreEqual(
            MetadataProviderFailureKind.Authentication,
            thrown.Kind);
    }

    [TestMethod]
    public async Task GetManualDetailsAsync_UsesCandidateOwner()
    {
        var first = FakeProvider.Empty("first");
        var owner = FakeProvider.WithMovie(
            [new("owner", "movie", "7", MediaType.Movie)],
            MovieDetails("소유 후보", "7", "owner"));
        var service = CreateService(first, owner);

        var details = await service.GetManualDetailsAsync(
            new("owner", "movie", "7", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual("소유 후보", details.Title);
        Assert.AreEqual(0, first.DetailsCalls);
        Assert.AreEqual(1, owner.DetailsCalls);

        var missing = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => service.GetManualDetailsAsync(
                new("missing", "movie", "7", MediaType.Movie),
                CancellationToken.None));
        Assert.AreEqual(
            MetadataProviderFailureKind.InvalidResponse,
            missing.Kind);
    }

    [TestMethod]
    public async Task ManualTvBrowsing_UsesCandidateProvider()
    {
        var unused = FakeProvider.Empty("unused");
        var selected = new FakeProvider(
            "selected",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
            (_, _) => throw new AssertFailedException("상세 조회를 호출하면 안 됩니다."),
            (_, _) => Task.FromResult<IReadOnlyList<TvSeasonCandidate>>(
                [new(0, "Specials", 1)]),
            (_, _, _) => Task.FromResult<IReadOnlyList<TvEpisodeCandidate>>(
                [new(1, "특별편", new DateOnly(2001, 1, 1))]));
        var service = CreateService(unused, selected);
        var series = new MetadataCandidate(
            "selected", "tv-series", "1431", MediaType.TvEpisode);

        var seasons = await service.GetManualTvSeasonsAsync(
            series, CancellationToken.None);
        var episodes = await service.GetManualTvEpisodesAsync(
            series, 0, CancellationToken.None);

        Assert.AreEqual(0, seasons.Single().SeasonNumber);
        Assert.AreEqual(1, episodes.Single().EpisodeNumber);
        Assert.AreEqual(0, unused.TvBrowseCalls);
        Assert.AreEqual(2, selected.TvBrowseCalls);
    }

    private MetadataEnrichmentService CreateService(
        params IMetadataProvider[] providers) =>
        new(
            new MediaFilenameParser(),
            providers,
            new LibraryStore(_root.FullName),
            _imageClient);

    private MetadataEnrichmentService CreateTimedService(
        IReadOnlyList<IMetadataProvider> providers,
        HttpClient imageClient,
        TimeSpan itemBudget) =>
        new(
            new MediaFilenameParser(),
            providers,
            new LibraryStore(_root.FullName),
            imageClient,
            itemBudget,
            () => DateTimeOffset.UtcNow,
            Task.Delay);

    private static Dictionary<string, VideoRecord> Records(
        params (string Path, MetadataStatus Status)[] values) =>
        values.ToDictionary(
            value => Path.GetFullPath(value.Path),
            value => new VideoRecord
            {
                Title = Path.GetFileNameWithoutExtension(value.Path),
                MetadataStatus = value.Status
            },
            StringComparer.OrdinalIgnoreCase);

    private static MetadataDetails MovieDetails(
        string title,
        string id,
        string providerKey = "fake") => new(
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
        PosterUri: null,
        ProviderReferences: [new(providerKey, "movie", id)]);

    private static MetadataDetails TvDetails(
        string seriesTitle,
        string episodeTitle,
        string providerKey) => new(
        MediaType: MediaType.TvEpisode,
        Title: null,
        OriginalTitle: seriesTitle,
        SeriesTitle: seriesTitle,
        EpisodeTitle: episodeTitle,
        ReleaseDate: new DateOnly(2024, 1, 1),
        Genres: ["드라마"],
        Director: "감독",
        Actors: ["배우"],
        Synopsis: "줄거리",
        SeasonNumber: 2,
        EpisodeNumber: 3,
        PosterUri: null,
        ProviderReferences:
        [
            new(providerKey, "series", "series-1"),
            new(providerKey, "episode", "episode-1")
        ]);

    private async Task<(
        VideoRecord Record,
        MetadataRunSummary Summary,
        MetadataProgress Progress)> RunSingleAsync(
        IMetadataProvider[] providers,
        string path,
        VideoRecord? current = null,
        HttpClient? imageClient = null,
        TimeSpan? itemBudget = null,
        bool requireSuccess = false)
    {
        var fullPath = Path.GetFullPath(path);
        var records = new Dictionary<string, VideoRecord>(
            StringComparer.OrdinalIgnoreCase)
        {
            [fullPath] = current ?? new VideoRecord
            {
                Title = Path.GetFileNameWithoutExtension(path),
                MetadataStatus = MetadataStatus.Pending
            }
        };
        VideoRecord? committed = null;
        var progress = new List<MetadataProgress>();
        var service = itemBudget is null
            ? new MetadataEnrichmentService(
                new MediaFilenameParser(),
                providers,
                new LibraryStore(_root.FullName),
                imageClient ?? _imageClient)
            : CreateTimedService(
                providers,
                imageClient ?? _imageClient,
                itemBudget.Value);
        var summary = await service.EnrichAsync(
            records,
            records.Keys.ToArray(),
            (_, record, _, _) =>
            {
                committed = record;
                return Task.CompletedTask;
            },
            progress.Add,
            CancellationToken.None,
            requireSuccess ? records.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase) : null);
        return (committed!, summary, progress.Single());
    }

    private static FakeProvider ProviderReturningAfterCancellation(
        MetadataDetails details) =>
        new(
            "fake",
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new("fake", "movie", "1", MediaType.Movie)
            ]),
            async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                }
                return details;
            });

    private static FakeProvider TvProvider(
        string key,
        MetadataDetails details) =>
        new(
            key,
            (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>(
            [
                new(
                    key,
                    "episode",
                    "1",
                    MediaType.TvEpisode,
                    details.SeasonNumber,
                    details.EpisodeNumber)
            ]),
            (_, _) => Task.FromResult(details));

    private sealed class FakeProvider(
        string providerKey,
        Func<MetadataQuery, CancellationToken,
            Task<IReadOnlyList<MetadataCandidate>>> search,
        Func<MetadataCandidate, CancellationToken, Task<MetadataDetails>> details,
        Func<MetadataCandidate, CancellationToken,
            Task<IReadOnlyList<TvSeasonCandidate>>>? seasons = null,
        Func<MetadataCandidate, int, CancellationToken,
            Task<IReadOnlyList<TvEpisodeCandidate>>>? episodes = null)
        : IMetadataProvider
    {
        public string ProviderKey { get; } = providerKey;
        private int _searchCalls;
        private int _detailsCalls;
        private int _tvBrowseCalls;

        public int SearchCalls => Volatile.Read(ref _searchCalls);
        public int DetailsCalls => Volatile.Read(ref _detailsCalls);
        public int TvBrowseCalls => Volatile.Read(ref _tvBrowseCalls);

        public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
            MetadataQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _searchCalls);
            return await search(query, cancellationToken);
        }

        public async Task<MetadataDetails> GetDetailsAsync(
            MetadataCandidate candidate,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _detailsCalls);
            return await details(candidate, cancellationToken);
        }

        public async Task<IReadOnlyList<TvSeasonCandidate>> GetTvSeasonsAsync(
            MetadataCandidate series,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _tvBrowseCalls);
            return seasons is null
                ? []
                : await seasons(series, cancellationToken);
        }

        public async Task<IReadOnlyList<TvEpisodeCandidate>> GetTvEpisodesAsync(
            MetadataCandidate series,
            int seasonNumber,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _tvBrowseCalls);
            return episodes is null
                ? []
                : await episodes(series, seasonNumber, cancellationToken);
        }

        internal static FakeProvider WithMovie(
            IReadOnlyList<MetadataCandidate> candidates,
            MetadataDetails details) =>
            new(
                candidates[0].ProviderKey,
                (_, _) => Task.FromResult(candidates),
                (_, _) => Task.FromResult(details));

        internal static FakeProvider Empty(string key) =>
            new(
                key,
                (_, _) => Task.FromResult<IReadOnlyList<MetadataCandidate>>([]),
                (_, _) => throw new AssertFailedException(
                    "상세 조회를 호출하면 안 됩니다."));

        internal static FakeProvider Failing(
            string key,
            MetadataProviderException error) =>
            new(
                key,
                (_, _) => Task.FromException<IReadOnlyList<MetadataCandidate>>(error),
                (_, _) => throw new AssertFailedException(
                    "상세 조회를 호출하면 안 됩니다."));
    }

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
