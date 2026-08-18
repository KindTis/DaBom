using Dabom.Library;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;

namespace Dabom.Metadata;

public sealed record MetadataProgress(
    string Path,
    int Completed,
    int Total,
    int Matched,
    int NotFound,
    int Failed,
    MetadataStatus Status,
    bool CommitSucceeded);

public sealed record RatingsProgress(int Completed, int Total);

public sealed record MetadataRunSummary(
    int Matched,
    int NotFound,
    int Failed,
    bool AuthenticationFailed,
    RatingsFailureKind? RatingsFailure = null);

public sealed class MetadataEnrichmentService
{
    private static readonly TimeSpan DefaultItemBudget = TimeSpan.FromSeconds(10);
    private readonly MediaFilenameParser _parser;
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly LibraryStore _store;
    private readonly HttpClient _imageClient;
    private readonly OmdbRatingsClient? _ratingsClient;
    private readonly TimeSpan _itemBudget;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MetadataEnrichmentService(
        MediaFilenameParser parser,
        IReadOnlyList<IMetadataProvider> providers,
        LibraryStore store,
        HttpClient imageClient)
        : this(
            parser,
            providers,
            store,
            imageClient,
            DefaultItemBudget,
            () => DateTimeOffset.UtcNow,
            Task.Delay)
    {
    }

    internal MetadataEnrichmentService(
        MediaFilenameParser parser,
        IReadOnlyList<IMetadataProvider> providers,
        LibraryStore store,
        HttpClient imageClient,
        OmdbRatingsClient ratingsClient)
        : this(
            parser,
            providers,
            store,
            imageClient,
            DefaultItemBudget,
            () => DateTimeOffset.UtcNow,
            Task.Delay,
            ratingsClient)
    {
    }

    internal MetadataEnrichmentService(
        MediaFilenameParser parser,
        IReadOnlyList<IMetadataProvider> providers,
        LibraryStore store,
        HttpClient imageClient,
        TimeSpan itemBudget,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        OmdbRatingsClient? ratingsClient = null)
    {
        _parser = parser;
        _providers = providers;
        _store = store;
        _imageClient = imageClient;
        _ratingsClient = ratingsClient;
        _itemBudget = itemBudget;
        _utcNow = utcNow;
        _delay = delay;
    }

    public async Task<IReadOnlyList<MetadataCandidate>> SearchManualAsync(
        string title,
        CancellationToken cancellationToken)
    {
        var unavailableUntil =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var deadline = _utcNow() + _itemBudget;
        using var budget = new CancellationTokenSource(_itemBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budget.Token);
        MetadataProviderException? failure = null;

        foreach (var provider in _providers)
        {
            try
            {
                var candidates = await ExecuteProviderCallAsync(
                    provider,
                    token => provider.SearchAsync(
                        new(MediaType.Unknown, title),
                        token),
                    deadline,
                    unavailableUntil,
                    linked.Token,
                    cancellationToken);
                if (candidates.Count > 0)
                {
                    return candidates;
                }
            }
            catch (MetadataProviderException error)
            {
                failure ??= error;
            }
            catch (OperationCanceledException)
                when (budget.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
            {
                failure ??= new(
                    MetadataProviderFailureKind.Transient,
                    "메타데이터 검색 시간이 초과되었습니다.");
            }
        }

        if (failure is not null)
        {
            throw failure;
        }

        return [];
    }

    public async Task<MetadataDetails> GetManualDetailsAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        var ratingsState = new RatingsRunState(_ratingsClient);
        var provider = _providers.FirstOrDefault(provider =>
            string.Equals(
                provider.ProviderKey,
                candidate.ProviderKey,
                StringComparison.Ordinal))
            ?? throw new MetadataProviderException(
                MetadataProviderFailureKind.InvalidResponse,
                "검색 후보를 제공한 메타데이터 공급자를 찾을 수 없습니다.");
        var unavailableUntil =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var deadline = _utcNow() + _itemBudget;
        using var budget = new CancellationTokenSource(_itemBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budget.Token);

        try
        {
            var details = await ExecuteProviderCallAsync(
                provider,
                token => provider.GetDetailsAsync(candidate, token),
                deadline,
                unavailableUntil,
                linked.Token,
                cancellationToken);
            if (!IsComplete(provider, candidate.MediaType, details))
            {
                throw new MetadataProviderException(
                    MetadataProviderFailureKind.InvalidResponse,
                    "선택한 메타데이터 상세 정보가 완전하지 않습니다.");
            }
            return details with
            {
                Ratings = await LookupRatingsAsync(
                    details.ImdbId,
                    ratingsState,
                    linked.Token,
                    cancellationToken)
            };
        }
        catch (OperationCanceledException)
            when (budget.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            throw new MetadataProviderException(
                MetadataProviderFailureKind.Transient,
                "메타데이터 상세 조회 시간이 초과되었습니다.");
        }
    }

    public Task<IReadOnlyList<TvSeasonCandidate>> GetManualTvSeasonsAsync(
        MetadataCandidate series,
        CancellationToken cancellationToken) =>
        ExecuteManualProviderCallAsync(
            series,
            (provider, token) => provider.GetTvSeasonsAsync(series, token),
            cancellationToken);

    public Task<IReadOnlyList<TvEpisodeCandidate>> GetManualTvEpisodesAsync(
        MetadataCandidate series,
        int seasonNumber,
        CancellationToken cancellationToken) =>
        ExecuteManualProviderCallAsync(
            series,
            (provider, token) => provider.GetTvEpisodesAsync(
                series, seasonNumber, token),
            cancellationToken);

    internal Task<string> DownloadPosterAsync(
        Uri source,
        CancellationToken cancellationToken) =>
        _store.DownloadPosterAsync(_imageClient, source, cancellationToken);

    public async Task<MetadataRunSummary> EnrichAsync(
        IReadOnlyDictionary<string, VideoRecord> records,
        IReadOnlyCollection<string> currentPaths,
        Func<string, VideoRecord, string?, CancellationToken, Task> commitAsync,
        Action<MetadataProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? requiredSuccessPaths = null,
        IReadOnlyDictionary<string, VideoRecord>? ratingsBackfillSnapshot = null,
        Action<RatingsProgress>? ratingsProgress = null)
    {
        var targets = currentPaths
            .Where(path => records.TryGetValue(path, out var record)
                && record.MetadataStatus is MetadataStatus.Pending
                    or MetadataStatus.Failed)
            .ToArray();
        var unavailableUntil =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using var commitGate = new SemaphoreSlim(1, 1);
        var matched = 0;
        var notFound = 0;
        var failed = 0;
        var authenticationFailed = false;
        var ratingsState = new RatingsRunState(_ratingsClient);
        var completed = 0;

        try
        {
            await Parallel.ForEachAsync(
                targets,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = cancellationToken
                },
                async (path, token) =>
                {
                var current = records[path];
                var requireSuccess = requiredSuccessPaths?.Contains(path) == true;
                var query = _parser.Parse(path);
                VideoRecord updated;
                string? createdPoster = null;
                var itemAuthenticationFailed = false;

                if (query is null)
                {
                    updated = current with
                    {
                        MetadataStatus = MetadataStatus.NotFound
                    };
                }
                else
                {
                    var deadline = _utcNow() + _itemBudget;
                    using var budget = new CancellationTokenSource(_itemBudget);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        token,
                        budget.Token);
                    MetadataDetails? selected = null;
                    var hadProviderError = false;

                    foreach (var provider in _providers)
                    {
                        if (unavailableUntil.TryGetValue(
                                provider.ProviderKey,
                                out var until)
                            && until > _utcNow())
                        {
                            hadProviderError = true;
                            continue;
                        }

                        try
                        {
                            var candidates = await ExecuteProviderCallAsync(
                                provider,
                                providerToken => provider.SearchAsync(
                                    query,
                                    providerToken),
                                deadline,
                                unavailableUntil,
                                linked.Token,
                                token);
                            if (candidates.Count == 0) continue;
                            var candidate = candidates[0];
                            if (candidate.MediaType != query.MediaType)
                            {
                                hadProviderError = true;
                                continue;
                            }

                            var details = await ExecuteProviderCallAsync(
                                provider,
                                providerToken => provider.GetDetailsAsync(
                                    candidate,
                                    providerToken),
                                deadline,
                                unavailableUntil,
                                linked.Token,
                                token);
                            token.ThrowIfCancellationRequested();
                            if (!IsComplete(provider, query.MediaType, details))
                            {
                                hadProviderError = true;
                                continue;
                            }

                            if (details.OptionalIssue is { } issue)
                            {
                                itemAuthenticationFailed |=
                                    issue.Kind
                                    == MetadataProviderFailureKind.Authentication;
                                if (issue.RetryAfter is { } retryAfter)
                                {
                                    ExtendUnavailableUntil(
                                        unavailableUntil,
                                        provider.ProviderKey,
                                        retryAfter);
                                }
                            }

                            selected = details;
                            break;
                        }
                        catch (MetadataProviderException error)
                        {
                            token.ThrowIfCancellationRequested();
                            hadProviderError = true;
                            itemAuthenticationFailed |=
                                error.Kind
                                == MetadataProviderFailureKind.Authentication;
                            if (error.RetryAfter is { } retryAfter)
                            {
                                ExtendUnavailableUntil(
                                    unavailableUntil,
                                    provider.ProviderKey,
                                    retryAfter);
                            }
                        }
                        catch (OperationCanceledException)
                            when (budget.IsCancellationRequested
                                && !token.IsCancellationRequested)
                        {
                            hadProviderError = true;
                            break;
                        }
                    }

                    if (selected is null)
                    {
                        updated = current with
                        {
                            MetadataStatus = hadProviderError
                                ? MetadataStatus.Failed
                                : MetadataStatus.NotFound
                        };
                    }
                    else
                    {
                        (updated, createdPoster) = await ApplyDetailsAsync(
                            current,
                            selected,
                            requireSuccess,
                            linked.Token,
                            token);
                        if (updated.MetadataStatus == MetadataStatus.Matched)
                        {
                            try
                            {
                                updated = ApplyRatings(
                                    current,
                                    updated,
                                    await LookupRatingsAsync(
                                        selected.ImdbId,
                                        ratingsState,
                                        linked.Token,
                                        token));
                            }
                            catch (OperationCanceledException)
                                when (token.IsCancellationRequested)
                            {
                                TryDeletePoster(createdPoster);
                                throw;
                            }
                        }
                    }
                }

                if (requireSuccess
                    && updated.MetadataStatus != MetadataStatus.Matched)
                {
                    updated = updated with
                    {
                        MetadataStatus = MetadataStatus.Failed
                    };
                }

                try
                {
                    await commitGate.WaitAsync(token);
                }
                catch
                {
                    TryDeletePoster(createdPoster);
                    throw;
                }

                try
                {
                    var commitSucceeded = false;
                    try
                    {
                        await commitAsync(path, updated, createdPoster, token);
                        commitSucceeded = true;
                        switch (updated.MetadataStatus)
                        {
                            case MetadataStatus.Matched:
                                matched++;
                                break;
                            case MetadataStatus.NotFound:
                                notFound++;
                                break;
                            default:
                                failed++;
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                        when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        failed++;
                    }

                    authenticationFailed |= itemAuthenticationFailed;
                    completed++;
                    progress?.Invoke(new(
                        path,
                        completed,
                        targets.Length,
                        matched,
                        notFound,
                        failed,
                        updated.MetadataStatus,
                        commitSucceeded));
                }
                finally
                {
                    commitGate.Release();
                }
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        var currentPathSet = currentPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ratingsTargets = ratingsBackfillSnapshot?
            .Where(pair => currentPathSet.Contains(pair.Key)
                && IsRatingsBackfillCandidate(pair.Value))
            .Select(pair => pair.Key)
            .ToArray()
            ?? [];
        if (ratingsTargets.Length > 0)
        {
            ratingsProgress?.Invoke(new(0, ratingsTargets.Length));
            if (ratingsState.KeyFailure is { } keyFailure)
            {
                RecordFailure(ratingsState, keyFailure);
            }
            else if (_ratingsClient is not null
                && ratingsState.UnavailableFailure is null)
            {
                var tmdbProvider = _providers.FirstOrDefault(provider =>
                    string.Equals(
                        provider.ProviderKey,
                        "tmdb",
                        StringComparison.Ordinal));
                var ratingsCompleted = 0;
                await Parallel.ForEachAsync(
                    ratingsTargets,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 3,
                        CancellationToken = cancellationToken
                    },
                    async (path, token) =>
                    {
                        if (ratingsState.UnavailableFailure is not null) return;
                        var current = records[path];
                        var imdbId = OmdbRatingsClient.IsValidImdbId(current.ImdbId)
                            ? current.ImdbId
                            : null;
                        var externalIdFailed = false;
                        var itemAuthenticationFailed = false;
                        using var budget = new CancellationTokenSource(_itemBudget);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                            token,
                            budget.Token);

                        if (imdbId is null)
                        {
                            if (tmdbProvider is null
                                || (unavailableUntil.TryGetValue(
                                        tmdbProvider.ProviderKey,
                                        out var until)
                                    && until > _utcNow()))
                            {
                                externalIdFailed = true;
                            }
                            else
                            {
                                try
                                {
                                    imdbId = await tmdbProvider.GetImdbIdAsync(
                                        current,
                                        linked.Token);
                                }
                                catch (MetadataProviderException error)
                                {
                                    token.ThrowIfCancellationRequested();
                                    externalIdFailed = true;
                                    itemAuthenticationFailed = error.Kind
                                        == MetadataProviderFailureKind.Authentication;
                                    if (error.RetryAfter is { } retryAfter)
                                    {
                                        ExtendUnavailableUntil(
                                            unavailableUntil,
                                            tmdbProvider.ProviderKey,
                                            retryAfter);
                                    }
                                }
                                catch (OperationCanceledException)
                                    when (budget.IsCancellationRequested
                                        && !token.IsCancellationRequested)
                                {
                                    externalIdFailed = true;
                                }
                            }
                        }

                        var updated = externalIdFailed
                            ? current
                            : ApplyRatings(
                                current,
                                current,
                                await LookupRatingsAsync(
                                    imdbId,
                                    ratingsState,
                                    linked.Token,
                                    token));
                        await commitGate.WaitAsync(token);
                        try
                        {
                            try
                            {
                                await commitAsync(path, updated, null, token);
                            }
                            catch (OperationCanceledException)
                                when (token.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch
                            {
                            }

                            authenticationFailed |= itemAuthenticationFailed;
                            ratingsProgress?.Invoke(new(
                                ++ratingsCompleted,
                                ratingsTargets.Length));
                        }
                        finally
                        {
                            commitGate.Release();
                        }
                    });
            }
        }

        return new(
            matched,
            notFound,
            failed,
            authenticationFailed,
            ratingsState.Failure);
    }

    private static bool IsRatingsBackfillCandidate(VideoRecord record)
    {
        if (record.MetadataStatus != MetadataStatus.Matched
            || record.RatingsFetched)
        {
            return false;
        }

        return record.MediaType switch
        {
            MediaType.Movie => HasValidTmdbReference(record, "movie"),
            MediaType.TvEpisode =>
                record.SeasonNumber is >= 0
                && record.EpisodeNumber is > 0
                && HasValidTmdbReference(record, "tv-series"),
            _ => false
        };
    }

    private static bool HasValidTmdbReference(
        VideoRecord record,
        string resourceType)
    {
        var matches = record.ProviderReferences.Where(reference =>
            reference.ProviderKey == "tmdb"
            && reference.ResourceType == resourceType).Take(2).ToArray();
        return matches.Length == 1
            && int.TryParse(
                matches[0].ResourceId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id)
            && id > 0;
    }

    private async Task<RatingsLookupResult?> LookupRatingsAsync(
        string? imdbId,
        RatingsRunState state,
        CancellationToken linkedToken,
        CancellationToken cancellationToken)
    {
        if (_ratingsClient is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return new(null, null, null, true);
        }
        if (!OmdbRatingsClient.IsValidImdbId(imdbId))
        {
            RecordFailure(state, RatingsFailureKind.InvalidResponse);
            return new(
                imdbId,
                null,
                null,
                false,
                RatingsFailureKind.InvalidResponse);
        }

        var start = StartRequest(state, imdbId, linkedToken);
        if (start.Request is null)
        {
            var failure = start.Failure!.Value;
            RecordFailure(state, failure);
            return new(imdbId, null, null, false, failure);
        }

        RatingsLookupResult result;
        try
        {
            result = await start.Request;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = new(
                imdbId,
                null,
                null,
                false,
                RatingsFailureKind.Transient);
        }

        if (!result.Fetched)
        {
            result = result with { ImdbId = imdbId };
            if (result.Failure is { } failure)
            {
                RecordFailure(state, failure);
            }
        }
        return result;
    }

    private static (
        Task<RatingsLookupResult>? Request,
        RatingsFailureKind? Failure) StartRequest(
            RatingsRunState state,
            string imdbId,
            CancellationToken cancellationToken)
    {
        lock (state.RequestStartGate)
        {
            var unavailable = state.UnavailableFailure;
            if (unavailable is not null || state.ApiKey is null)
            {
                return (null, unavailable ?? state.KeyFailure);
            }

            return (
                state.Client!.FetchAsync(
                    state.ApiKey,
                    imdbId,
                    cancellationToken),
                null);
        }
    }

    private static void RecordFailure(
        RatingsRunState state,
        RatingsFailureKind failure)
    {
        if (failure == RatingsFailureKind.Configuration)
        {
            Volatile.Write(ref state.ConfigurationFailed, 1);
        }
        Interlocked.CompareExchange(
            ref state.FailureValue,
            (int)failure + 1,
            0);
        if (failure is RatingsFailureKind.Authentication
            or RatingsFailureKind.RateLimited)
        {
            lock (state.RequestStartGate)
            {
                Interlocked.CompareExchange(
                    ref state.StopFailure,
                    (int)failure + 1,
                    0);
            }
        }
    }

    private void TryDeletePoster(string? poster)
    {
        try { _store.DeletePoster(poster); }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException) { }
    }

    private async Task<T> ExecuteManualProviderCallAsync<T>(
        MetadataCandidate candidate,
        Func<IMetadataProvider, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(provider =>
            string.Equals(
                provider.ProviderKey,
                candidate.ProviderKey,
                StringComparison.Ordinal))
            ?? throw new MetadataProviderException(
                MetadataProviderFailureKind.InvalidResponse,
                "검색 후보를 제공한 메타데이터 공급자를 찾을 수 없습니다.");
        var unavailableUntil =
            new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var deadline = _utcNow() + _itemBudget;
        using var budget = new CancellationTokenSource(_itemBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budget.Token);

        try
        {
            return await ExecuteProviderCallAsync(
                provider,
                token => call(provider, token),
                deadline,
                unavailableUntil,
                linked.Token,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (budget.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            throw new MetadataProviderException(
                MetadataProviderFailureKind.Transient,
                "메타데이터 목록 조회 시간이 초과되었습니다.");
        }
    }

    private async Task<T> ExecuteProviderCallAsync<T>(
        IMetadataProvider provider,
        Func<CancellationToken, Task<T>> call,
        DateTimeOffset deadline,
        ConcurrentDictionary<string, DateTimeOffset> unavailableUntil,
        CancellationToken linkedToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call(linkedToken);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (MetadataProviderException error)
            {
                if (error.RetryAfter is { } retryAfter)
                {
                    ExtendUnavailableUntil(
                        unavailableUntil,
                        provider.ProviderKey,
                        retryAfter);
                }

                if (error.Kind != MetadataProviderFailureKind.Transient
                    || attempt >= 2)
                {
                    throw;
                }

                var wait = error.RetryAfter
                    ?? TimeSpan.FromMilliseconds(attempt == 0 ? 250 : 500);
                if (wait >= deadline - _utcNow())
                {
                    throw;
                }

                try
                {
                    await _delay(wait, linkedToken);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }
    }

    private void ExtendUnavailableUntil(
        ConcurrentDictionary<string, DateTimeOffset> unavailableUntil,
        string providerKey,
        TimeSpan retryAfter)
    {
        var observedUntil = _utcNow() + retryAfter;
        unavailableUntil.AddOrUpdate(
            providerKey,
            observedUntil,
            (_, current) => current >= observedUntil ? current : observedUntil);
    }

    private async Task<(VideoRecord Record, string? CreatedPoster)>
        ApplyDetailsAsync(
            VideoRecord current,
            MetadataDetails details,
            bool requirePoster,
            CancellationToken linkedToken,
            CancellationToken cancellationToken)
    {
        var mediaType = Keep(
            current,
            MetadataField.MediaType,
            current.MediaType,
            details.MediaType);
        var seriesTitle = Keep(
            current,
            MetadataField.SeriesTitle,
            current.SeriesTitle,
            details.SeriesTitle);
        var episodeTitle = Keep(
            current,
            MetadataField.EpisodeTitle,
            current.EpisodeTitle,
            details.EpisodeTitle);
        var seasonNumber = Keep(
            current,
            MetadataField.SeasonNumber,
            current.SeasonNumber,
            details.SeasonNumber);
        var episodeNumber = Keep(
            current,
            MetadataField.EpisodeNumber,
            current.EpisodeNumber,
            details.EpisodeNumber);
        var fetchedTitle = mediaType == MediaType.TvEpisode
            ? BuildEpisodeTitle(
                seriesTitle,
                episodeTitle,
                seasonNumber,
                episodeNumber)
            : details.Title;
        var updated = current with
        {
            Title = Keep(
                current,
                MetadataField.Title,
                current.Title,
                fetchedTitle),
            OriginalTitle = Keep(
                current,
                MetadataField.OriginalTitle,
                current.OriginalTitle,
                details.OriginalTitle),
            SeriesTitle = seriesTitle,
            EpisodeTitle = episodeTitle,
            ReleaseDate = Keep(
                current,
                MetadataField.ReleaseDate,
                current.ReleaseDate,
                details.ReleaseDate),
            Genres = Keep(
                current,
                MetadataField.Genres,
                current.Genres,
                details.Genres),
            Director = Keep(
                current,
                MetadataField.Director,
                current.Director,
                details.Director),
            Actors = Keep(
                current,
                MetadataField.Actors,
                current.Actors,
                details.Actors),
            Synopsis = Keep(
                current,
                MetadataField.Synopsis,
                current.Synopsis,
                details.Synopsis),
            MediaType = mediaType,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            ProviderReferences = details.ProviderReferences
        };

        string? createdPoster = null;
        var poster = current.Poster;
        var posterProtected =
            current.UserEditedFields.Contains(MetadataField.Poster);
        var posterFailed = !posterProtected
            && (details.PosterFailed
                || requirePoster && details.PosterUri is null);
        if (!posterProtected && !posterFailed)
        {
            if (details.PosterUri is null)
            {
                poster = null;
            }
            else
            {
                try
                {
                    linkedToken.ThrowIfCancellationRequested();
                    createdPoster = await _store.DownloadPosterAsync(
                        _imageClient,
                        details.PosterUri,
                        linkedToken);
                    poster = createdPoster;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    posterFailed = true;
                    poster = current.Poster;
                }
            }
        }

        return (updated with
        {
            Poster = poster,
            MetadataStatus = posterFailed
                ? MetadataStatus.Failed
                : MetadataStatus.Matched
        }, createdPoster);
    }

    private static bool IsComplete(
        IMetadataProvider provider,
        MediaType expectedType,
        MetadataDetails details)
    {
        if (details.MediaType != expectedType) return false;

        var expectedReferences = expectedType switch
        {
            MediaType.Movie => 1,
            MediaType.TvEpisode => 2,
            _ => 0
        };
        var ownsRequiredReferences =
            details.ProviderReferences.Length == expectedReferences
            && details.ProviderReferences.All(reference =>
                string.Equals(
                    reference.ProviderKey,
                    provider.ProviderKey,
                    StringComparison.Ordinal));

        return ownsRequiredReferences
            && (expectedType switch
            {
                MediaType.Movie => !string.IsNullOrWhiteSpace(details.Title),
                MediaType.TvEpisode =>
                    !string.IsNullOrWhiteSpace(details.SeriesTitle)
                    && details.SeasonNumber is not null
                    && details.EpisodeNumber is not null,
                _ => false
            });
    }

    private static T Keep<T>(
        VideoRecord current,
        MetadataField field,
        T currentValue,
        T fetchedValue) =>
        current.UserEditedFields.Contains(field)
            ? currentValue
            : fetchedValue;

    internal static VideoRecord ApplyRatings(
        VideoRecord current,
        VideoRecord updated,
        RatingsLookupResult? result)
    {
        if (result is null) return updated;
        if (result.Fetched)
        {
            return updated with
            {
                ImdbId = result.ImdbId,
                ImdbRating = result.ImdbRating,
                RottenTomatoesRating = result.RottenTomatoesRating,
                RatingsFetched = true
            };
        }

        var sameId = !string.IsNullOrWhiteSpace(result.ImdbId)
            && string.Equals(
                current.ImdbId,
                result.ImdbId,
                StringComparison.Ordinal);
        return updated with
        {
            ImdbId = result.ImdbId,
            ImdbRating = sameId ? current.ImdbRating : null,
            RottenTomatoesRating = sameId
                ? current.RottenTomatoesRating
                : null,
            RatingsFetched = false
        };
    }

    private sealed class RatingsRunState
    {
        internal readonly object RequestStartGate = new();
        internal readonly OmdbRatingsClient? Client;
        internal int ConfigurationFailed;
        internal int FailureValue;
        internal int StopFailure;

        internal RatingsRunState(OmdbRatingsClient? client)
        {
            Client = client;
            if (client is null) return;
            (ApiKey, KeyFailure) = client.ReadApiKey();
        }

        internal string? ApiKey { get; }
        internal RatingsFailureKind? KeyFailure { get; }
        internal RatingsFailureKind? UnavailableFailure =>
            Read(Volatile.Read(ref StopFailure));
        internal RatingsFailureKind? Failure =>
            UnavailableFailure
            ?? (Volatile.Read(ref ConfigurationFailed) != 0
                ? RatingsFailureKind.Configuration
                : Read(Volatile.Read(ref FailureValue)));

        private static RatingsFailureKind? Read(int value) =>
            value == 0 ? null : (RatingsFailureKind)(value - 1);
    }

    internal static string BuildEpisodeTitle(
        string? seriesTitle,
        string? episodeTitle,
        int? seasonNumber,
        int? episodeNumber)
    {
        var title = $"{seriesTitle} S{seasonNumber:00}E{episodeNumber:00}";
        return string.IsNullOrWhiteSpace(episodeTitle)
            ? title
            : $"{title} · {episodeTitle}";
    }
}
