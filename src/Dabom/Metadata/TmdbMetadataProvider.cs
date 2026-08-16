using Dabom.Library;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dabom.Metadata;

internal static class TmdbAccessToken
{
    internal static string? ReadFromLocalApplicationData(
        string localApplicationDataPath)
    {
        var envPath = Path.Combine(
            Path.GetFullPath(localApplicationDataPath),
            "Dabom",
            ".env");
        if (!File.Exists(envPath)) return null;
        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            const string prefix = "DABOM_TMDB_ACCESS_TOKEN=";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim().Trim('"');
            }
        }

        return null;
    }
}

public sealed class TmdbMetadataProvider : IMetadataProvider
{
    private static readonly Uri ApiRoot =
        new("https://api.themoviedb.org/3/");
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private readonly Func<string?> _getAccessToken;
    private readonly SemaphoreSlim _imageConfigurationGate = new(1, 1);
    private ImageConfiguration? _imageConfiguration;

    public TmdbMetadataProvider(
        HttpClient client,
        Func<string?> getAccessToken)
    {
        _client = client;
        _getAccessToken = getAccessToken;
    }

    public string ProviderKey => "tmdb";

    public async Task<IReadOnlyList<TvSeasonCandidate>> GetTvSeasonsAsync(
        MetadataCandidate series,
        CancellationToken cancellationToken)
    {
        var seriesId = GetTvSeriesId(series);
        var response = await SendJsonAsync<TvSeasonsResponse>(
            $"tv/{seriesId}?language=ko-KR",
            cancellationToken);
        ValidateId(response.Id, seriesId, "TV 시리즈");
        var seasons = response.Seasons
            ?? throw InvalidResponse("TMDB TV 시즌 목록 형식이 올바르지 않습니다.");

        return seasons.Select(season =>
        {
            if (season.SeasonNumber < 0 || season.EpisodeCount < 0)
            {
                throw InvalidResponse("TMDB TV 시즌 정보가 올바르지 않습니다.");
            }

            return new TvSeasonCandidate(
                season.SeasonNumber,
                string.IsNullOrWhiteSpace(season.Name)
                    ? $"시즌 {season.SeasonNumber}"
                    : season.Name.Trim(),
                season.EpisodeCount);
        }).ToArray();
    }

    public async Task<IReadOnlyList<TvEpisodeCandidate>> GetTvEpisodesAsync(
        MetadataCandidate series,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var seriesId = GetTvSeriesId(series);
        if (seasonNumber < 0)
        {
            throw InvalidResponse("TMDB TV 시즌 번호가 올바르지 않습니다.");
        }

        var response = await SendJsonAsync<TvSeasonEpisodesResponse>(
            $"tv/{seriesId}/season/{seasonNumber}?language=ko-KR",
            cancellationToken);
        var episodes = response.Episodes
            ?? throw InvalidResponse("TMDB TV 에피소드 목록 형식이 올바르지 않습니다.");

        return episodes.Select(episode =>
        {
            if (episode.EpisodeNumber < 1)
            {
                throw InvalidResponse("TMDB TV 에피소드 번호가 올바르지 않습니다.");
            }

            return new TvEpisodeCandidate(
                episode.EpisodeNumber,
                string.IsNullOrWhiteSpace(episode.Name)
                    ? $"{episode.EpisodeNumber}화"
                    : episode.Name.Trim(),
                ParseDate(episode.AirDate));
        }).ToArray();
    }

    public async Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        MetadataQuery query,
        CancellationToken cancellationToken)
    {
        if (query.MediaType == MediaType.Unknown)
        {
            var multiResponse = await SendJsonAsync<SearchResponse>(
                $"search/multi?query={Uri.EscapeDataString(query.Title)}&language=ko-KR",
                cancellationToken);
            var multiResults = multiResponse.Results
                ?? throw InvalidResponse("TMDB 검색 결과 형식이 올바르지 않습니다.");
            var candidates = new List<MetadataCandidate>();
            var canLoadPoster = true;

            foreach (var result in multiResults.Where(result =>
                result.MediaType is "movie" or "tv"))
            {
                if (result.Id <= 0)
                {
                    throw InvalidResponse("TMDB 검색 결과 ID가 올바르지 않습니다.");
                }

                var isMovie = result.MediaType == "movie";
                var displayTitle = isMovie ? result.Title : result.Name;
                if (string.IsNullOrWhiteSpace(displayTitle))
                {
                    throw InvalidResponse("TMDB 검색 결과 제목이 없습니다.");
                }

                Uri? posterUri = null;
                if (canLoadPoster && !string.IsNullOrWhiteSpace(result.PosterPath))
                {
                    try
                    {
                        posterUri = await GetPosterUriAsync(
                            result.PosterPath,
                            cancellationToken);
                    }
                    catch (MetadataProviderException)
                    {
                        canLoadPoster = false;
                    }
                }

                candidates.Add(new(
                    ProviderKey,
                    isMovie ? "movie" : "tv-series",
                    result.Id.ToString(CultureInfo.InvariantCulture),
                    isMovie ? MediaType.Movie : MediaType.TvEpisode,
                    DisplayTitle: displayTitle.Trim(),
                    OriginalTitle: NullIfWhiteSpace(
                        isMovie ? result.OriginalTitle : result.OriginalName),
                    Year: ParseDate(
                        isMovie ? result.ReleaseDate : result.FirstAirDate)?.Year,
                    PosterUri: posterUri));
            }

            return candidates;
        }

        var escaped = Uri.EscapeDataString(query.Title);
        var path = query.MediaType switch
        {
            MediaType.Movie =>
                $"search/movie?query={escaped}&language=ko-KR"
                + (query.Year is int year
                    ? $"&primary_release_year={year}"
                    : string.Empty),
            MediaType.TvEpisode =>
                $"search/tv?query={escaped}&language=ko-KR",
            _ => throw InvalidResponse("지원하지 않는 미디어 형식입니다.")
        };
        var response = await SendJsonAsync<SearchResponse>(
            path,
            cancellationToken);
        var results = response.Results
            ?? throw InvalidResponse("TMDB 검색 결과 형식이 올바르지 않습니다.");
        if (results.Any(result => result.Id <= 0))
        {
            throw InvalidResponse("TMDB 검색 결과 ID가 올바르지 않습니다.");
        }

        return results.Select(result => new MetadataCandidate(
            ProviderKey,
            query.MediaType == MediaType.Movie ? "movie" : "tv-series",
            result.Id.ToString(CultureInfo.InvariantCulture),
            query.MediaType,
            query.SeasonNumber,
            query.EpisodeNumber)).ToArray();
    }

    public Task<MetadataDetails> GetDetailsAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                candidate.ProviderKey,
                ProviderKey,
                StringComparison.Ordinal)
            || !int.TryParse(
                candidate.ResourceId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id)
            || id <= 0)
        {
            throw InvalidResponse("TMDB 후보 참조가 올바르지 않습니다.");
        }

        return candidate.MediaType switch
        {
            MediaType.Movie when candidate.ResourceType == "movie" =>
                GetMovieDetailsAsync(id, cancellationToken),
            MediaType.TvEpisode when candidate.ResourceType == "tv-series"
                && candidate.SeasonNumber is int season
                && candidate.EpisodeNumber is int episode =>
                GetTvDetailsAsync(
                    id,
                    season,
                    episode,
                    cancellationToken),
            _ => throw InvalidResponse("TMDB 후보 자원 형식이 올바르지 않습니다.")
        };
    }

    private async Task<MetadataDetails> GetMovieDetailsAsync(
        int movieId,
        CancellationToken cancellationToken)
    {
        var korean = await SendJsonAsync<MovieDetailsResponse>(
            $"movie/{movieId}?language=ko-KR",
            cancellationToken);
        ValidateId(korean.Id, movieId, "영화");
        var title = korean.Title;
        var synopsis = korean.Overview;
        MetadataProviderIssue? issue = null;

        if (string.IsNullOrWhiteSpace(title))
        {
            var english = await SendJsonAsync<MovieDetailsResponse>(
                $"movie/{movieId}?language=en-US",
                cancellationToken);
            ValidateId(english.Id, movieId, "영화");
            title = english.Title;
            if (string.IsNullOrWhiteSpace(synopsis))
            {
                synopsis = english.Overview;
            }
        }
        else if (string.IsNullOrWhiteSpace(synopsis))
        {
            var english = await TryOptionalAsync(
                async token =>
                {
                    var value = await SendJsonAsync<MovieDetailsResponse>(
                        $"movie/{movieId}?language=en-US",
                        token);
                    ValidateId(value.Id, movieId, "영화");
                    return value;
                },
                cancellationToken);
            issue = MergeIssue(issue, english.Issue);
            if (english.Value is { } value)
            {
                synopsis = value.Overview;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw InvalidResponse("TMDB 영화 제목이 없습니다.");
        }

        var credits = await TryOptionalAsync(
            token => SendJsonAsync<CreditsResponse>(
                $"movie/{movieId}/credits?language=ko-KR",
                token),
            cancellationToken);
        issue = MergeIssue(issue, credits.Issue);
        var cast = credits.Value?.Cast ?? [];
        var crew = credits.Value?.Crew ?? [];
        var actors = ActorNames(cast);
        var director = crew.FirstOrDefault(person =>
            person.Job == "Director"
            && !string.IsNullOrWhiteSpace(person.Name))?.Name;

        Uri? posterUri = null;
        var posterFailed = false;
        if (!string.IsNullOrWhiteSpace(korean.PosterPath))
        {
            var poster = await TryOptionalAsync(
                token => GetPosterUriAsync(korean.PosterPath, token),
                cancellationToken);
            issue = MergeIssue(issue, poster.Issue);
            posterUri = poster.Value;
            posterFailed = poster.Issue is not null;
        }

        return new(
            MediaType: MediaType.Movie,
            Title: title,
            OriginalTitle: korean.OriginalTitle,
            SeriesTitle: null,
            EpisodeTitle: null,
            ReleaseDate: ParseDate(korean.ReleaseDate),
            Genres: GenreNames(korean.Genres),
            Director: director,
            Actors: actors,
            Synopsis: NullIfWhiteSpace(synopsis),
            SeasonNumber: null,
            EpisodeNumber: null,
            PosterUri: posterUri,
            ProviderReferences:
            [
                new(
                    ProviderKey,
                    "movie",
                    movieId.ToString(CultureInfo.InvariantCulture))
            ],
            PosterFailed: posterFailed,
            OptionalIssue: issue);
    }

    private async Task<MetadataDetails> GetTvDetailsAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        var series = await SendJsonAsync<TvSeriesDetailsResponse>(
            $"tv/{seriesId}?language=ko-KR",
            cancellationToken);
        ValidateId(series.Id, seriesId, "TV 시리즈");
        var seriesTitle = series.Name;
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            var englishSeries =
                await SendJsonAsync<TvSeriesDetailsResponse>(
                    $"tv/{seriesId}?language=en-US",
                    cancellationToken);
            ValidateId(englishSeries.Id, seriesId, "TV 시리즈");
            seriesTitle = englishSeries.Name;
        }
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            throw InvalidResponse("TMDB TV 시리즈 제목이 없습니다.");
        }

        var episodePath =
            $"tv/{seriesId}/season/{seasonNumber}/episode/{episodeNumber}";
        var episode = await SendJsonAsync<TvEpisodeDetailsResponse>(
            $"{episodePath}?language=ko-KR",
            cancellationToken);
        if (episode.Id <= 0)
        {
            throw InvalidResponse("TMDB TV 에피소드 ID가 올바르지 않습니다.");
        }

        MetadataProviderIssue? issue = null;
        Uri? posterUri = null;
        var posterFailed = false;
        if (!string.IsNullOrWhiteSpace(series.PosterPath))
        {
            var poster = await TryOptionalAsync(
                token => GetPosterUriAsync(series.PosterPath, token),
                cancellationToken);
            issue = MergeIssue(issue, poster.Issue);
            posterUri = poster.Value;
            posterFailed = poster.Issue is not null;
        }

        var episodeTitle = episode.Name;
        var synopsis = episode.Overview;
        if (string.IsNullOrWhiteSpace(episodeTitle)
            || string.IsNullOrWhiteSpace(synopsis))
        {
            var englishEpisode = await TryOptionalAsync(
                async token =>
                {
                    var value = await SendJsonAsync<TvEpisodeDetailsResponse>(
                        $"{episodePath}?language=en-US",
                        token);
                    if (value.Id <= 0)
                    {
                        throw InvalidResponse(
                            "TMDB TV 에피소드 ID가 올바르지 않습니다.");
                    }
                    return value;
                },
                cancellationToken);
            issue = MergeIssue(issue, englishEpisode.Issue);
            if (englishEpisode.Value is { } value)
            {
                if (string.IsNullOrWhiteSpace(episodeTitle))
                {
                    episodeTitle = value.Name;
                }
                if (string.IsNullOrWhiteSpace(synopsis))
                {
                    synopsis = value.Overview;
                }
            }
        }

        var episodeCredits = await TryOptionalAsync(
            token => SendJsonAsync<CreditsResponse>(
                $"{episodePath}/credits?language=ko-KR",
                token),
            cancellationToken);
        issue = MergeIssue(issue, episodeCredits.Issue);

        var seriesCredits = await TryOptionalAsync(
            token => SendJsonAsync<CreditsResponse>(
                $"tv/{seriesId}/credits?language=ko-KR",
                token),
            cancellationToken);
        issue = MergeIssue(issue, seriesCredits.Issue);

        var actors = ActorNames(
            (episodeCredits.Value?.Cast ?? [])
            .Concat(episode.GuestStars ?? []));
        var director = (episode.Crew ?? []).FirstOrDefault(person =>
            person.Job == "Director"
            && !string.IsNullOrWhiteSpace(person.Name))?.Name;

        return new(
            MediaType: MediaType.TvEpisode,
            Title: null,
            OriginalTitle: series.OriginalName,
            SeriesTitle: seriesTitle,
            EpisodeTitle: NullIfWhiteSpace(episodeTitle),
            ReleaseDate: ParseDate(episode.AirDate),
            Genres: GenreNames(series.Genres),
            Director: director,
            Actors: actors,
            Synopsis: NullIfWhiteSpace(synopsis),
            SeasonNumber: seasonNumber,
            EpisodeNumber: episodeNumber,
            PosterUri: posterUri,
            ProviderReferences:
            [
                new(
                    ProviderKey,
                    "tv-series",
                    seriesId.ToString(CultureInfo.InvariantCulture)),
                new(
                    ProviderKey,
                    "tv-episode",
                    episode.Id.ToString(CultureInfo.InvariantCulture))
            ],
            PosterFailed: posterFailed,
            OptionalIssue: issue);
    }

    private async Task<Uri?> GetPosterUriAsync(
        string posterPath,
        CancellationToken cancellationToken)
    {
        var configuration = _imageConfiguration;
        if (configuration is null)
        {
            await _imageConfigurationGate.WaitAsync(cancellationToken);
            try
            {
                configuration = _imageConfiguration;
                if (configuration is null)
                {
                    var response = await SendJsonAsync<ConfigurationResponse>(
                        "configuration",
                        cancellationToken);
                    var images = response.Images
                        ?? throw InvalidResponse(
                            "TMDB 이미지 설정 형식이 올바르지 않습니다.");
                    if (!Uri.TryCreate(
                            images.SecureBaseUrl,
                            UriKind.Absolute,
                            out var secureBase)
                        || secureBase.Scheme != Uri.UriSchemeHttps
                        || images.PosterSizes is null)
                    {
                        throw InvalidResponse(
                            "TMDB 이미지 설정 형식이 올바르지 않습니다.");
                    }

                    configuration = new(
                        secureBase,
                        images.PosterSizes.Contains(
                            "w500",
                            StringComparer.Ordinal));
                    _imageConfiguration = configuration;
                }
            }
            finally
            {
                _imageConfigurationGate.Release();
            }
        }

        if (!configuration.HasW500) return null;
        return new Uri(
            configuration.SecureBaseUrl,
            $"w500/{posterPath.TrimStart('/')}");
    }

    private async Task<OptionalResult<T>> TryOptionalAsync<T>(
        Func<CancellationToken, Task<T>> request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(
                default,
                new(MetadataProviderFailureKind.Transient));
        }

        try
        {
            return new(await request(cancellationToken), null);
        }
        catch (MetadataProviderException error)
        {
            return new(
                default,
                new(error.Kind, error.RetryAfter));
        }
        catch (OperationCanceledException)
        {
            return new(
                default,
                new(MetadataProviderFailureKind.Transient));
        }
    }

    private async Task<T> SendJsonAsync<T>(
        string relative,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(relative);
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new MetadataProviderException(
                MetadataProviderFailureKind.Transient,
                "TMDB 네트워크 요청에 실패했습니다.",
                innerException: error);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.Authentication,
                    $"TMDB 인증에 실패했습니다. HTTP {(int)response.StatusCode}");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.Transient,
                    "TMDB 요청 제한에 도달했습니다. HTTP 429",
                    ReadRetryAfter(response.Headers.RetryAfter));
            }
            if ((int)response.StatusCode >= 500)
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.Transient,
                    $"TMDB 서버 요청에 실패했습니다. HTTP {(int)response.StatusCode}");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.InvalidResponse,
                    $"TMDB 요청이 거부되었습니다. HTTP {(int)response.StatusCode}");
            }

            try
            {
                await using var stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                    ?? throw InvalidResponse(
                        "TMDB 응답 내용이 비어 있습니다.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (
                error is JsonException or NotSupportedException)
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.InvalidResponse,
                    "TMDB JSON 응답 형식이 올바르지 않습니다.",
                    innerException: error);
            }
        }
    }

    private HttpRequestMessage CreateRequest(string relative)
    {
        string? accessToken;
        try
        {
            accessToken = _getAccessToken();
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            throw new MetadataProviderException(
                MetadataProviderFailureKind.Authentication,
                ".env의 DABOM_TMDB_ACCESS_TOKEN을 읽을 수 없습니다.",
                innerException: error);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MetadataProviderException(
                MetadataProviderFailureKind.Authentication,
                ".env의 DABOM_TMDB_ACCESS_TOKEN이 없습니다.");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(ApiRoot, relative));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static TimeSpan? ReadRetryAfter(
        RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta) return delta;
        if (retryAfter?.Date is not { } date) return null;
        var remaining = date - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static MetadataProviderIssue? MergeIssue(
        MetadataProviderIssue? current,
        MetadataProviderIssue? next)
    {
        if (next is null) return current;
        if (current is null) return next;
        if (current.Kind != MetadataProviderFailureKind.Authentication
            && next.Kind == MetadataProviderFailureKind.Authentication)
        {
            return next;
        }
        if (current.Kind == MetadataProviderFailureKind.Authentication
            && next.Kind != MetadataProviderFailureKind.Authentication)
        {
            return current;
        }
        if (current.Kind != next.Kind) return current;
        return new(
            current.Kind,
            Max(current.RetryAfter, next.RetryAfter));
    }

    private static TimeSpan? Max(TimeSpan? left, TimeSpan? right) =>
        left is null ? right
        : right is null ? left
        : left >= right ? left
        : right;

    private static string[] ActorNames(IEnumerable<PersonResponse> people) =>
        people
            .Where(person =>
                person.Id > 0 && !string.IsNullOrWhiteSpace(person.Name))
            .DistinctBy(person => person.Id)
            .Take(10)
            .Select(person => person.Name!.Trim())
            .ToArray();

    private static string[] GenreNames(GenreResponse[]? genres) =>
        (genres ?? [])
            .Select(genre => genre.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }
        throw InvalidResponse("TMDB 날짜 형식이 올바르지 않습니다.");
    }

    private int GetTvSeriesId(MetadataCandidate series)
    {
        if (!string.Equals(series.ProviderKey, ProviderKey, StringComparison.Ordinal)
            || series.ResourceType != "tv-series"
            || !int.TryParse(
                series.ResourceId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id)
            || id <= 0)
        {
            throw InvalidResponse("TMDB TV 시리즈 후보 참조가 올바르지 않습니다.");
        }

        return id;
    }

    private static void ValidateId(int actual, int expected, string resource)
    {
        if (actual != expected || actual <= 0)
        {
            throw InvalidResponse($"TMDB {resource} ID가 올바르지 않습니다.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MetadataProviderException InvalidResponse(string message) =>
        new(MetadataProviderFailureKind.InvalidResponse, message);

    private sealed record OptionalResult<T>(
        T? Value,
        MetadataProviderIssue? Issue);

    private sealed record ImageConfiguration(
        Uri SecureBaseUrl,
        bool HasW500);

    private sealed record SearchResponse
    {
        [JsonPropertyName("results")]
        public SearchResult[]? Results { get; init; }
    }

    private sealed record SearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; init; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }
    }

    private sealed record MovieDetailsResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; init; }

        [JsonPropertyName("overview")]
        public string? Overview { get; init; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; init; }

        [JsonPropertyName("genres")]
        public GenreResponse[]? Genres { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }
    }

    private sealed record TvSeriesDetailsResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; init; }

        [JsonPropertyName("overview")]
        public string? Overview { get; init; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; init; }

        [JsonPropertyName("genres")]
        public GenreResponse[]? Genres { get; init; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; init; }
    }

    private sealed record TvSeasonsResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("seasons")]
        public TvSeasonResponse[]? Seasons { get; init; }
    }

    private sealed record TvSeasonResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; init; }

        [JsonPropertyName("episode_count")]
        public int EpisodeCount { get; init; }
    }

    private sealed record TvSeasonEpisodesResponse
    {
        [JsonPropertyName("episodes")]
        public TvSeasonEpisodeResponse[]? Episodes { get; init; }
    }

    private sealed record TvSeasonEpisodeResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; init; }

        [JsonPropertyName("air_date")]
        public string? AirDate { get; init; }
    }

    private sealed record TvEpisodeDetailsResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("overview")]
        public string? Overview { get; init; }

        [JsonPropertyName("air_date")]
        public string? AirDate { get; init; }

        [JsonPropertyName("guest_stars")]
        public PersonResponse[]? GuestStars { get; init; }

        [JsonPropertyName("crew")]
        public PersonResponse[]? Crew { get; init; }
    }

    private sealed record CreditsResponse
    {
        [JsonPropertyName("cast")]
        public PersonResponse[]? Cast { get; init; }

        [JsonPropertyName("crew")]
        public PersonResponse[]? Crew { get; init; }
    }

    private sealed record PersonResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("job")]
        public string? Job { get; init; }
    }

    private sealed record GenreResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed record ConfigurationResponse
    {
        [JsonPropertyName("images")]
        public ConfigurationImagesResponse? Images { get; init; }
    }

    private sealed record ConfigurationImagesResponse
    {
        [JsonPropertyName("secure_base_url")]
        public string? SecureBaseUrl { get; init; }

        [JsonPropertyName("poster_sizes")]
        public string[]? PosterSizes { get; init; }
    }
}
