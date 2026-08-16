using Dabom.Library;
using System.Collections.Concurrent;
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

public sealed record MetadataRunSummary(
    int Matched,
    int NotFound,
    int Failed,
    bool AuthenticationFailed);

public sealed class MetadataEnrichmentService
{
    private static readonly TimeSpan DefaultItemBudget = TimeSpan.FromSeconds(10);
    private readonly MediaFilenameParser _parser;
    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly LibraryStore _store;
    private readonly HttpClient _imageClient;
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
        TimeSpan itemBudget,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _parser = parser;
        _providers = providers;
        _store = store;
        _imageClient = imageClient;
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
            return details;
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
        IReadOnlySet<string>? requiredSuccessPaths = null)
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
                    try { _store.DeletePoster(createdPoster); }
                    catch (Exception error) when (
                        error is IOException or UnauthorizedAccessException) { }
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

        return new(matched, notFound, failed, authenticationFailed);
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
