using Dabom.Library;

namespace Dabom.Metadata;

public sealed record MetadataQuery(
    MediaType MediaType,
    string Title,
    int? Year = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

public sealed record MetadataCandidate(
    string ProviderKey,
    string ResourceType,
    string ResourceId,
    MediaType MediaType,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    string? DisplayTitle = null,
    string? OriginalTitle = null,
    int? Year = null,
    Uri? PosterUri = null);

public sealed record TvSeasonCandidate(
    int SeasonNumber,
    string Name,
    int EpisodeCount);

public sealed record TvEpisodeCandidate(
    int EpisodeNumber,
    string Name,
    DateOnly? AirDate);

public enum MetadataProviderFailureKind
{
    Authentication,
    Transient,
    InvalidResponse
}

public enum RatingsFailureKind
{
    MissingKey,
    Configuration,
    Authentication,
    RateLimited,
    Transient,
    InvalidResponse
}

public sealed record RatingsLookupResult(
    string? ImdbId,
    double? ImdbRating,
    int? RottenTomatoesRating,
    bool Fetched,
    RatingsFailureKind? Failure = null);

public sealed record MetadataProviderIssue(
    MetadataProviderFailureKind Kind,
    TimeSpan? RetryAfter = null);

public sealed record MetadataDetails(
    MediaType MediaType,
    string? Title,
    string? OriginalTitle,
    string? SeriesTitle,
    string? EpisodeTitle,
    DateOnly? ReleaseDate,
    string[] Genres,
    string? Director,
    string[] Actors,
    string? Synopsis,
    int? SeasonNumber,
    int? EpisodeNumber,
    Uri? PosterUri,
    ProviderReference[] ProviderReferences,
    bool PosterFailed = false,
    MetadataProviderIssue? OptionalIssue = null);

public sealed class MetadataProviderException(
    MetadataProviderFailureKind kind,
    string message,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public MetadataProviderFailureKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public interface IMetadataProvider
{
    string ProviderKey { get; }

    Task<IReadOnlyList<MetadataCandidate>> SearchAsync(
        MetadataQuery query,
        CancellationToken cancellationToken);

    Task<MetadataDetails> GetDetailsAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TvSeasonCandidate>> GetTvSeasonsAsync(
        MetadataCandidate series,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TvSeasonCandidate>>([]);

    Task<IReadOnlyList<TvEpisodeCandidate>> GetTvEpisodesAsync(
        MetadataCandidate series,
        int seasonNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TvEpisodeCandidate>>([]);
}
