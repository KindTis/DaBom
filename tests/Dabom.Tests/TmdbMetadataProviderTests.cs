using Dabom.Library;
using Dabom.Metadata;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dabom.Tests;

[TestClass]
public sealed class TmdbMetadataProviderTests
{
    [TestMethod]
    public async Task GetTvSeasonsAsync_ReturnsProviderSeasonNamesIncludingZero()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"id":1431,"seasons":[
              {"name":"Specials","season_number":0,"episode_count":12},
              {"name":"시즌 1","season_number":1,"episode_count":23}
            ]}
            """));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var seasons = await provider.GetTvSeasonsAsync(
            new("tmdb", "tv-series", "1431", MediaType.TvEpisode),
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "0:Specials:12", "1:시즌 1:23" },
            seasons.Select(value =>
                $"{value.SeasonNumber}:{value.Name}:{value.EpisodeCount}").ToArray());
        Assert.AreEqual("/3/tv/1431?language=ko-KR", handler.Requests.Single().Uri.PathAndQuery);
    }

    [TestMethod]
    public async Task GetTvEpisodesAsync_ReturnsEpisodeNamesAndAirDates()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"id":99,"season_number":0,"episodes":[
              {"id":100,"name":"특별편","episode_number":1,"air_date":"2001-01-01"}
            ]}
            """));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var episodes = await provider.GetTvEpisodesAsync(
            new("tmdb", "tv-series", "1431", MediaType.TvEpisode),
            0,
            CancellationToken.None);

        Assert.AreEqual(1, episodes.Single().EpisodeNumber);
        Assert.AreEqual("특별편", episodes.Single().Name);
        Assert.AreEqual(new DateOnly(2001, 1, 1), episodes.Single().AirDate);
        Assert.AreEqual(
            "/3/tv/1431/season/0?language=ko-KR",
            handler.Requests.Single().Uri.PathAndQuery);
    }

    [TestMethod]
    public async Task SearchMovieAsync_UsesBearerKoreanQueryAndYear()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"results":[{"id":597,"title":"타이타닉"}]}
            """));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "secret-token");

        var result = await provider.SearchAsync(
            new(MediaType.Movie, "Titanic", 1997),
            CancellationToken.None);

        Assert.AreEqual("597", result.Single().ResourceId);
        var request = handler.Requests.Single();
        Assert.AreEqual("Bearer", request.Authorization?.Scheme);
        Assert.AreEqual("secret-token", request.Authorization?.Parameter);
        StringAssert.Contains(request.Uri.Query, "query=Titanic");
        StringAssert.Contains(request.Uri.Query, "primary_release_year=1997");
        StringAssert.Contains(request.Uri.Query, "language=ko-KR");
    }

    [TestMethod]
    public async Task SearchAsync_Unknown_MapsMovieAndTvAndSkipsUnsupportedResults()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "results": [
                    {
                      "id": 496243,
                      "media_type": "movie",
                      "title": "기생충",
                      "original_title": "Parasite",
                      "release_date": "2019-05-30",
                      "poster_path": "/movie.jpg"
                    },
                    {
                      "id": 1396,
                      "media_type": "tv",
                      "name": "브레이킹 배드",
                      "original_name": "Breaking Bad",
                      "first_air_date": "2008-01-20",
                      "poster_path": null
                    },
                    {
                      "id": 1,
                      "media_type": "person",
                      "name": "지원하지 않는 인물"
                    }
                  ]
                }
                """),
            Json("""
                {
                  "images": {
                    "secure_base_url": "https://image.tmdb.org/t/p/",
                    "poster_sizes": ["w500"]
                  }
                }
                """)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var candidates = await provider.SearchAsync(
            new(MediaType.Unknown, "기생충"),
            CancellationToken.None);

        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(MediaType.Movie, candidates[0].MediaType);
        Assert.AreEqual("movie", candidates[0].ResourceType);
        Assert.AreEqual("기생충", candidates[0].DisplayTitle);
        Assert.AreEqual("Parasite", candidates[0].OriginalTitle);
        Assert.AreEqual(2019, candidates[0].Year);
        Assert.AreEqual(
            "https://image.tmdb.org/t/p/w500/movie.jpg",
            candidates[0].PosterUri!.AbsoluteUri);
        Assert.AreEqual(MediaType.TvEpisode, candidates[1].MediaType);
        Assert.AreEqual("tv-series", candidates[1].ResourceType);
        Assert.AreEqual("브레이킹 배드", candidates[1].DisplayTitle);
        Assert.AreEqual("Breaking Bad", candidates[1].OriginalTitle);
        Assert.AreEqual(2008, candidates[1].Year);
        CollectionAssert.AreEqual(
            new[] { "/3/search/multi", "/3/configuration" },
            RequestPaths(handler, includeQueryForSearch: false));
    }

    [TestMethod]
    public async Task SearchAsync_Unknown_WhenConfigurationFails_ReturnsTextWithoutPoster()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(
                "/configuration",
                StringComparison.Ordinal)
                ? Json("{}", HttpStatusCode.ServiceUnavailable)
                : Json("""
                    {
                      "results": [
                        {
                          "id": 496243,
                          "media_type": "movie",
                          "title": "기생충",
                          "original_title": "Parasite",
                          "release_date": "2019-05-30",
                          "poster_path": "/movie.jpg"
                        }
                      ]
                    }
                    """));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var candidate = (await provider.SearchAsync(
            new(MediaType.Unknown, "기생충"),
            CancellationToken.None)).Single();

        Assert.AreEqual("기생충", candidate.DisplayTitle);
        Assert.IsNull(candidate.PosterUri);
    }

    [TestMethod]
    public async Task GetDetailsAsync_MapsMovieAndRequestsCreditsBeforeConfiguration()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id": 1,
                  "title": "한국어 제목",
                  "original_title": "Original Title",
                  "overview": "한국어 줄거리",
                  "release_date": "2024-07-25",
                  "genres": [{"id":1,"name":"드라마"},{"id":2,"name":"판타지"}],
                  "poster_path": "/poster.jpg"
                }
                """),
            Json(MovieCreditsJson(12)),
            Json("""
                {
                  "images": {
                    "secure_base_url": "https://image.tmdb.org/t/p/",
                    "poster_sizes": ["w342", "w500"]
                  }
                }
                """)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual("한국어 제목", details.Title);
        Assert.AreEqual("Original Title", details.OriginalTitle);
        Assert.AreEqual(new DateOnly(2024, 7, 25), details.ReleaseDate);
        CollectionAssert.AreEqual(
            new[] { "드라마", "판타지" },
            details.Genres);
        Assert.AreEqual("감독", details.Director);
        Assert.AreEqual(10, details.Actors.Length);
        Assert.AreEqual("한국어 줄거리", details.Synopsis);
        Assert.AreEqual(
            "movie",
            details.ProviderReferences.Single().ResourceType);
        Assert.AreEqual(
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            details.PosterUri!.AbsoluteUri);
        CollectionAssert.AreEqual(
            new[]
            {
                "/3/movie/1?language=ko-KR",
                "/3/movie/1/credits?language=ko-KR",
                "/3/configuration"
            },
            RequestPaths(handler));
    }

    [TestMethod]
    public async Task GetDetailsAsync_UsesConfirmedTvRequestOrderAndEpisodeActors()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""{"results":[{"id":10,"name":"시리즈"}]}"""),
            Json("""
                {
                  "id":10,
                  "name":"시리즈",
                  "original_name":"Series",
                  "overview":"시리즈 줄거리",
                  "first_air_date":"2024-01-01",
                  "genres":[{"id":1,"name":"드라마"}],
                  "poster_path":"/series.jpg"
                }
                """),
            Json("""
                {
                  "id":20,
                  "name":"회차",
                  "overview":"회차 줄거리",
                  "air_date":"2024-02-03",
                  "guest_stars":[
                    {"id":2,"name":"중복 게스트"},
                    {"id":3,"name":"게스트"}
                  ],
                  "crew":[{"id":30,"name":"에피소드 감독","job":"Director"}]
                }
                """),
            Json("""
                {
                  "images": {
                    "secure_base_url":"https://image.tmdb.org/t/p/",
                    "poster_sizes":["w500"]
                  }
                }
                """),
            Json("""
                {
                  "cast":[
                    {"id":1,"name":"주연"},
                    {"id":2,"name":"중복 배우"}
                  ],
                  "crew":[]
                }
                """),
            Json("""
                {
                  "cast":[{"id":99,"name":"시리즈 배우"}],
                  "crew":[]
                }
                """)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");
        var candidates = await provider.SearchAsync(
            new(MediaType.TvEpisode, "Series", null, 2, 3),
            CancellationToken.None);

        var details = await provider.GetDetailsAsync(
            candidates.Single(),
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "주연", "중복 배우", "게스트" },
            details.Actors);
        Assert.AreEqual("에피소드 감독", details.Director);
        Assert.AreEqual("시리즈", details.SeriesTitle);
        Assert.AreEqual("회차", details.EpisodeTitle);
        Assert.AreEqual(new DateOnly(2024, 2, 3), details.ReleaseDate);
        CollectionAssert.AreEqual(
            new[] { "tv-series", "tv-episode" },
            details.ProviderReferences
                .Select(reference => reference.ResourceType)
                .ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "/3/search/tv",
                "/3/tv/10?language=ko-KR",
                "/3/tv/10/season/2/episode/3?language=ko-KR",
                "/3/configuration",
                "/3/tv/10/season/2/episode/3/credits?language=ko-KR",
                "/3/tv/10/credits?language=ko-KR"
            },
            RequestPaths(handler, includeQueryForSearch: false));
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenKoreanTitleOrOverviewIsBlank_FillsOnlyBlankValuesFromEnglish()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":1,
                  "title":"한국어 제목",
                  "original_title":"Original",
                  "overview":"",
                  "release_date":"2024-01-01",
                  "genres":[],
                  "poster_path":null
                }
                """),
            Json("""
                {
                  "id":1,
                  "title":"English Title",
                  "original_title":"Original",
                  "overview":"English overview",
                  "release_date":"2024-01-01",
                  "genres":[],
                  "poster_path":null
                }
                """),
            Json("""{"cast":[],"crew":[]}""")
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual("한국어 제목", details.Title);
        Assert.AreEqual("English overview", details.Synopsis);
        CollectionAssert.AreEqual(
            new[]
            {
                "/3/movie/1?language=ko-KR",
                "/3/movie/1?language=en-US",
                "/3/movie/1/credits?language=ko-KR"
            },
            RequestPaths(handler));
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalEnglishMovieIdIsInvalid_ReturnsPartialDetailsWithIssue()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":1,"title":"한국어 제목","original_title":"Original",
                  "overview":"","release_date":"2024-01-01",
                  "genres":[],"poster_path":null
                }
                """),
            Json("""
                {
                  "id":2,"title":"English Title","original_title":"Original",
                  "overview":"English overview","release_date":"2024-01-01",
                  "genres":[],"poster_path":null
                }
                """),
            Json("""{"cast":[],"crew":[]}""")
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual("한국어 제목", details.Title);
        Assert.IsNull(details.Synopsis);
        Assert.AreEqual(
            MetadataProviderFailureKind.InvalidResponse,
            details.OptionalIssue?.Kind);
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalEnglishEpisodeIdIsInvalid_ReturnsPartialDetailsWithIssue()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            TvSeriesResponse(),
            Json("""
                {
                  "id":20,"name":"회차","overview":"",
                  "air_date":"2024-01-02","guest_stars":[],"crew":[]
                }
                """),
            Json("""
                {
                  "id":0,"name":"Episode","overview":"English overview",
                  "air_date":"2024-01-02","guest_stars":[],"crew":[]
                }
                """),
            Json("""{"cast":[],"crew":[]}"""),
            Json("""{"cast":[],"crew":[]}""")
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "tv-series", "10", MediaType.TvEpisode, 1, 2),
            CancellationToken.None);

        Assert.AreEqual("시리즈", details.SeriesTitle);
        Assert.AreEqual("회차", details.EpisodeTitle);
        Assert.IsNull(details.Synopsis);
        Assert.AreEqual(
            MetadataProviderFailureKind.InvalidResponse,
            details.OptionalIssue?.Kind);
        Assert.AreEqual(5, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetDetailsAsync_DoesNotUseSeriesCreditsAsEpisodeActors()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":10,"name":"시리즈","original_name":"Series",
                  "overview":"줄거리","first_air_date":"2024-01-01",
                  "genres":[],"poster_path":null
                }
                """),
            Json("""
                {
                  "id":20,"name":"회차","overview":"줄거리",
                  "air_date":"2024-01-02","guest_stars":[],"crew":[]
                }
                """),
            Json("""{"cast":[],"crew":[]}"""),
            Json("""{"cast":[{"id":99,"name":"시리즈 배우"}],"crew":[]}""")
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "tv-series", "10", MediaType.TvEpisode, 1, 2),
            CancellationToken.None);

        Assert.AreEqual(0, details.Actors.Length);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalCreditsFails_ReturnsCompleteDetailsWithIssue()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":1,"title":"제목","original_title":"Original",
                  "overview":"줄거리","release_date":"2024-01-01",
                  "genres":[],"poster_path":null
                }
                """),
            Json("{}", HttpStatusCode.ServiceUnavailable)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual("제목", details.Title);
        Assert.AreEqual(
            MetadataProviderFailureKind.Transient,
            details.OptionalIssue?.Kind);
        Assert.AreEqual(0, details.Actors.Length);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalRequestIsCanceled_ReturnsCompletePartialDetails()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (request, token) =>
        {
            if (request.RequestUri!.Query.Contains("ko-KR", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "id":1,"title":"제목","original_title":"Original",
                      "overview":"","release_date":"2024-01-01",
                      "genres":[],"poster_path":null
                    }
                    """);
            }

            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");
        using var cancellation = new CancellationTokenSource();

        var getDetails = provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        var details = await getDetails;

        Assert.AreEqual("제목", details.Title);
        Assert.AreEqual(
            MetadataProviderFailureKind.Transient,
            details.OptionalIssue?.Kind);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenRequiredEnglishTitleFails_ThrowsProviderException()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":10,"name":"","original_name":"Series",
                  "overview":"줄거리","first_air_date":"2024-01-01",
                  "genres":[],"poster_path":null
                }
                """),
            Json("{}", HttpStatusCode.ServiceUnavailable)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.GetDetailsAsync(
                new("tmdb", "tv-series", "10", MediaType.TvEpisode, 1, 2),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.Transient, error.Kind);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenConfigurationFails_MarksPosterFailed()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {
                  "id":1,"title":"제목","original_title":"Original",
                  "overview":"줄거리","release_date":"2024-01-01",
                  "genres":[],"poster_path":"/poster.jpg"
                }
                """),
            Json("""{"cast":[],"crew":[]}"""),
            Json("{}", HttpStatusCode.ServiceUnavailable)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.IsTrue(details.PosterFailed);
        Assert.IsNull(details.PosterUri);
        Assert.AreEqual(
            MetadataProviderFailureKind.Transient,
            details.OptionalIssue?.Kind);
    }

    [TestMethod]
    public async Task RequestAsync_Classifies429AndFiveHundredsWithoutInternalRetry()
    {
        var rateHandler = new RecordingHandler(_ =>
        {
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter =
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return response;
        });
        using var rateClient = new HttpClient(rateHandler);
        var rateProvider = new TmdbMetadataProvider(rateClient, () => "token");

        var rateError = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => rateProvider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.Transient, rateError.Kind);
        Assert.AreEqual(TimeSpan.FromSeconds(12), rateError.RetryAfter);
        Assert.AreEqual(1, rateHandler.Requests.Count);

        var serverHandler = new RecordingHandler(_ =>
            Json("{}", HttpStatusCode.BadGateway));
        using var serverClient = new HttpClient(serverHandler);
        var serverProvider = new TmdbMetadataProvider(serverClient, () => "token");

        var serverError = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => serverProvider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.Transient, serverError.Kind);
        Assert.AreEqual(1, serverHandler.Requests.Count);

        var networkHandler = new RecordingHandler(_ =>
            throw new HttpRequestException("offline"));
        using var networkClient = new HttpClient(networkHandler);
        var networkProvider = new TmdbMetadataProvider(networkClient, () => "token");

        var networkError = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => networkProvider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.Transient, networkError.Kind);
        Assert.AreEqual(1, networkHandler.Requests.Count);
    }

    [TestMethod]
    public async Task RequestAsync_DoesNotRetry401Or403()
    {
        foreach (var status in new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden
        })
        {
            var handler = new RecordingHandler(_ => Json("{}", status));
            using var client = new HttpClient(handler);
            var provider = new TmdbMetadataProvider(client, () => "token");

            var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
                () => provider.SearchAsync(
                    new(MediaType.Movie, "Movie"),
                    CancellationToken.None));

            Assert.AreEqual(MetadataProviderFailureKind.Authentication, error.Kind);
            Assert.AreEqual(1, handler.Requests.Count);
        }
    }

    [TestMethod]
    public async Task RequestAsync_UsesLatestAccessTokenFromAccessor()
    {
        var tokens = new Queue<string?>(["first-token", null, "third-token"]);
        var handler = new RecordingHandler(_ => Json("""{"results":[]}"""));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => tokens.Dequeue());

        await provider.SearchAsync(
            new(MediaType.Movie, "First"),
            CancellationToken.None);
        var missing = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.SearchAsync(
                new(MediaType.Movie, "Second"),
                CancellationToken.None));
        await provider.SearchAsync(
            new(MediaType.Movie, "Third"),
            CancellationToken.None);

        Assert.AreEqual(MetadataProviderFailureKind.Authentication, missing.Kind);
        CollectionAssert.AreEqual(
            new[] { "first-token", "third-token" },
            handler.Requests
                .Select(request => request.Authorization?.Parameter)
                .ToArray());
    }

    [TestMethod]
    public async Task RequestAsync_WhenTokenAccessorCannotReadEnv_ClassifiesAuthentication()
    {
        var handler = new RecordingHandler(_ => Json("""{"results":[]}"""));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(
            client,
            () => throw new IOException("locked"));

        var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.Authentication, error.Kind);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task RequestAsync_WhenJsonIsInvalid_ClassifiesInvalidResponse()
    {
        var handler = new RecordingHandler(_ => Json("{broken"));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.InvalidResponse, error.Kind);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ExceptionAndStatusNeverContainAccessToken()
    {
        const string token = "secret-token";
        var handler = new RecordingHandler(_ =>
            Json(token, HttpStatusCode.Unauthorized));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => token);

        var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.SearchAsync(
                new(MediaType.Movie, "Movie"),
                CancellationToken.None));

        Assert.IsFalse(
            error.ToString().Contains(token, StringComparison.Ordinal));
    }

    [TestMethod]
    public void TmdbAccessToken_ReadsOnlyNamedValueFromLocalAppDataEnv()
    {
        var root = Directory.CreateTempSubdirectory("dabom-token-");
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, ".env"),
                "DABOM_TMDB_ACCESS_TOKEN=wrong-location");
            var dabom = Directory.CreateDirectory(
                Path.Combine(root.FullName, "Dabom"));
            File.WriteAllText(
                Path.Combine(dabom.FullName, ".env"),
                """
                # 주석
                OTHER_TOKEN=wrong
                DABOM_TMDB_ACCESS_TOKEN_EXTRA=wrong
                DABOM_TMDB_ACCESS_TOKEN="right-token"
                """);

            var token = TmdbAccessToken.ReadFromLocalApplicationData(
                root.FullName);

            Assert.AreEqual("right-token", token);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task GetDetailsAsync_RejectsInvalidCandidateReferenceBeforeRequest()
    {
        var handler = new RecordingHandler(_ => Json("{}"));
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var error = await Assert.ThrowsExceptionAsync<MetadataProviderException>(
            () => provider.GetDetailsAsync(
                new("other", "movie", "0", MediaType.Movie),
                CancellationToken.None));

        Assert.AreEqual(MetadataProviderFailureKind.InvalidResponse, error.Kind);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetDetailsAsync_ReusesSuccessfulConfiguration()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            MovieResponse(1, "/first.jpg"),
            Json("""{"cast":[],"crew":[]}"""),
            Json("""
                {
                  "images":{
                    "secure_base_url":"https://image.tmdb.org/t/p/",
                    "poster_sizes":["w500"]
                  }
                }
                """),
            MovieResponse(2, "/second.jpg"),
            Json("""{"cast":[],"crew":[]}""")
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);
        await provider.GetDetailsAsync(
            new("tmdb", "movie", "2", MediaType.Movie),
            CancellationToken.None);

        Assert.AreEqual(
            1,
            handler.Requests.Count(request =>
                request.Uri.AbsolutePath == "/3/configuration"));
    }

    [TestMethod]
    public async Task GetDetailsAsync_ConcurrentCallsLoadConfigurationOnce()
    {
        var configurationCalls = 0;
        var configurationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConfiguration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/3/configuration")
            {
                Interlocked.Increment(ref configurationCalls);
                configurationStarted.TrySetResult();
                await releaseConfiguration.Task.WaitAsync(token);
                return Json("""
                    {"images":{"secure_base_url":"https://image.tmdb.org/t/p/","poster_sizes":["w500"]}}
                    """);
            }
            if (path.EndsWith("/credits", StringComparison.Ordinal))
            {
                return Json("""{"cast":[],"crew":[]}""");
            }
            var id = path.Contains("/movie/1", StringComparison.Ordinal) ? 1 : 2;
            return MovieResponse(id, $"/{id}.jpg");
        });
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var first = provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);
        var second = provider.GetDetailsAsync(
            new("tmdb", "movie", "2", MediaType.Movie),
            CancellationToken.None);

        await configurationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseConfiguration.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, configurationCalls);
        Assert.IsTrue(results.All(result => result.PosterUri is not null));
    }

    [TestMethod]
    public async Task GetDetailsAsync_CanceledConfigurationLoadLetsWaitingCallRetry()
    {
        var configurationCalls = 0;
        var firstConfigurationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/3/configuration")
            {
                if (Interlocked.Increment(ref configurationCalls) == 1)
                {
                    firstConfigurationStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return Json("""
                    {"images":{"secure_base_url":"https://image.tmdb.org/t/p/","poster_sizes":["w500"]}}
                    """);
            }
            if (path.EndsWith("/credits", StringComparison.Ordinal))
            {
                return Json("""{"cast":[],"crew":[]}""");
            }
            var id = path.Contains("/movie/1", StringComparison.Ordinal) ? 1 : 2;
            return MovieResponse(id, $"/{id}.jpg");
        });
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");
        using var firstBudget = new CancellationTokenSource();

        var first = provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            firstBudget.Token);
        await firstConfigurationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = provider.GetDetailsAsync(
            new("tmdb", "movie", "2", MediaType.Movie),
            CancellationToken.None);
        firstBudget.Cancel();
        var firstResult = await first;
        var secondResult = await second;

        Assert.IsTrue(firstResult.PosterFailed);
        Assert.IsNotNull(firstResult.OptionalIssue);
        Assert.IsNotNull(secondResult.PosterUri);
        Assert.IsFalse(secondResult.PosterFailed);
        Assert.AreEqual(2, configurationCalls);
    }

    [TestMethod]
    public async Task GetDetailsAsync_FailedConfigurationLoadLetsWaitingCallRetry()
    {
        var configurationCalls = 0;
        var firstConfigurationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCreditsLoaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstConfiguration = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/3/configuration")
            {
                if (Interlocked.Increment(ref configurationCalls) == 1)
                {
                    firstConfigurationStarted.TrySetResult();
                    await releaseFirstConfiguration.Task.WaitAsync(token);
                    return Json("{}", HttpStatusCode.ServiceUnavailable);
                }
                return Json("""
                    {"images":{"secure_base_url":"https://image.tmdb.org/t/p/","poster_sizes":["w500"]}}
                    """);
            }
            if (path.EndsWith("/credits", StringComparison.Ordinal))
            {
                if (path.Contains("/movie/2/", StringComparison.Ordinal))
                    secondCreditsLoaded.TrySetResult();
                return Json("""{"cast":[],"crew":[]}""");
            }
            var id = path.Contains("/movie/1", StringComparison.Ordinal) ? 1 : 2;
            return MovieResponse(id, $"/{id}.jpg");
        });
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var first = provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);
        await firstConfigurationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = provider.GetDetailsAsync(
            new("tmdb", "movie", "2", MediaType.Movie),
            CancellationToken.None);
        await secondCreditsLoaded.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFirstConfiguration.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.IsTrue(results[0].PosterFailed);
        Assert.AreEqual(
            MetadataProviderFailureKind.Transient,
            results[0].OptionalIssue?.Kind);
        Assert.IsNotNull(results[1].PosterUri);
        Assert.IsFalse(results[1].PosterFailed);
        Assert.AreEqual(2, configurationCalls);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenConfigurationLacksW500_OmitsPosterWithoutFailure()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            MovieResponse(1, "/poster.jpg"),
            Json("""{"cast":[],"crew":[]}"""),
            Json("""
                {
                  "images":{
                    "secure_base_url":"https://image.tmdb.org/t/p/",
                    "poster_sizes":["original"]
                  }
                }
                """)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "movie", "1", MediaType.Movie),
            CancellationToken.None);

        Assert.IsNull(details.PosterUri);
        Assert.IsFalse(details.PosterFailed);
        Assert.IsNull(details.OptionalIssue);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalIssuesDiffer_PrioritizesAuthentication()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            TvSeriesResponse(),
            TvEpisodeResponse(),
            Json("{}", HttpStatusCode.ServiceUnavailable),
            Json("{}", HttpStatusCode.Unauthorized)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "tv-series", "10", MediaType.TvEpisode, 1, 2),
            CancellationToken.None);

        Assert.AreEqual(
            MetadataProviderFailureKind.Authentication,
            details.OptionalIssue?.Kind);
    }

    [TestMethod]
    public async Task GetDetailsAsync_WhenOptionalRetryAfterDiffers_KeepsLongest()
    {
        var firstRateLimit = Json("{}", HttpStatusCode.TooManyRequests);
        firstRateLimit.Headers.RetryAfter =
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
        var secondRateLimit = Json("{}", HttpStatusCode.TooManyRequests);
        secondRateLimit.Headers.RetryAfter =
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
        var responses = new Queue<HttpResponseMessage>(
        [
            TvSeriesResponse(),
            TvEpisodeResponse(),
            firstRateLimit,
            secondRateLimit
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var client = new HttpClient(handler);
        var provider = new TmdbMetadataProvider(client, () => "token");

        var details = await provider.GetDetailsAsync(
            new("tmdb", "tv-series", "10", MediaType.TvEpisode, 1, 2),
            CancellationToken.None);

        Assert.AreEqual(
            MetadataProviderFailureKind.Transient,
            details.OptionalIssue?.Kind);
        Assert.AreEqual(
            TimeSpan.FromSeconds(5),
            details.OptionalIssue?.RetryAfter);
    }

    private static HttpResponseMessage MovieResponse(
        int id,
        string? posterPath) =>
        Json(JsonSerializer.Serialize(new
        {
            id,
            title = $"영화 {id}",
            original_title = $"Movie {id}",
            overview = "줄거리",
            release_date = "2024-01-01",
            genres = Array.Empty<object>(),
            poster_path = posterPath
        }));

    private static HttpResponseMessage TvSeriesResponse() =>
        Json("""
            {
              "id":10,"name":"시리즈","original_name":"Series",
              "overview":"줄거리","first_air_date":"2024-01-01",
              "genres":[],"poster_path":null
            }
            """);

    private static HttpResponseMessage TvEpisodeResponse() =>
        Json("""
            {
              "id":20,"name":"회차","overview":"줄거리",
              "air_date":"2024-01-02","guest_stars":[],"crew":[]
            }
            """);

    private static string MovieCreditsJson(int castCount) =>
        JsonSerializer.Serialize(new
        {
            cast = Enumerable.Range(1, castCount)
                .Select(id => new { id, name = $"배우 {id}" }),
            crew = new[]
            {
                new { id = 100, name = "감독", job = "Director" }
            }
        });

    private static string[] RequestPaths(
        RecordingHandler handler,
        bool includeQueryForSearch = true) =>
        handler.Requests.Select(request =>
        {
            if (!includeQueryForSearch
                && request.Uri.AbsolutePath.Contains(
                    "/search/",
                    StringComparison.Ordinal))
            {
                return request.Uri.AbsolutePath;
            }
            return request.Uri.PathAndQuery;
        }).ToArray();

    private sealed record RecordedRequest(
        Uri Uri,
        AuthenticationHeaderValue? Authorization);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _respond;

        internal RecordingHandler(
            Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        internal RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
                respond)
        {
            _respond = respond;
        }

        internal List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(new(
                    request.RequestUri!,
                    request.Headers.Authorization is { } authorization
                        ? new(
                            authorization.Scheme,
                            authorization.Parameter)
                        : null));
            }
            return _respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json")
        };
}
