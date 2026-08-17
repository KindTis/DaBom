using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dabom.Metadata;

internal sealed class OmdbRatingsClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private readonly Func<string?> _getApiKey;

    internal OmdbRatingsClient(HttpClient client, Func<string?> getApiKey)
    {
        _client = client;
        _getApiKey = getApiKey;
    }

    internal (string? ApiKey, RatingsFailureKind? Failure) ReadApiKey()
    {
        try
        {
            var key = _getApiKey();
            return string.IsNullOrWhiteSpace(key)
                ? (null, RatingsFailureKind.MissingKey)
                : (key.Trim(), null);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            return (null, RatingsFailureKind.Configuration);
        }
    }

    internal async Task<RatingsLookupResult> FetchAsync(
        string apiKey,
        string imdbId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(imdbId)
            || !imdbId.StartsWith("tt", StringComparison.Ordinal)
            || imdbId.Length <= 2
            || imdbId[2..].Any(character => character is < '0' or > '9'))
        {
            return Failed(RatingsFailureKind.InvalidResponse);
        }

        var uri = new Uri(
            $"https://www.omdbapi.com/?apikey={Uri.EscapeDataString(apiKey)}&i={Uri.EscapeDataString(imdbId)}&r=json");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failed(RatingsFailureKind.Transient);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                return Failed(RatingsFailureKind.Authentication);
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failed(RatingsFailureKind.RateLimited);
            }
            if ((int)response.StatusCode >= 500)
            {
                return Failed(RatingsFailureKind.Transient);
            }
            if (!response.IsSuccessStatusCode)
            {
                return Failed(RatingsFailureKind.InvalidResponse);
            }

            OmdbResponse? body;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                body = await JsonSerializer.DeserializeAsync<OmdbResponse>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (
                error is HttpRequestException or IOException)
            {
                return Failed(RatingsFailureKind.Transient);
            }
            catch (Exception error) when (
                error is JsonException or NotSupportedException)
            {
                return Failed(RatingsFailureKind.InvalidResponse);
            }

            if (body is null)
            {
                return Failed(RatingsFailureKind.InvalidResponse);
            }
            if (string.Equals(body.Response, "False", StringComparison.Ordinal))
            {
                return body.Error switch
                {
                    "Movie not found!" or "Incorrect IMDb ID." =>
                        new(imdbId, null, null, true),
                    "Invalid API key!" => Failed(RatingsFailureKind.Authentication),
                    "Request limit reached!" => Failed(RatingsFailureKind.RateLimited),
                    _ => Failed(RatingsFailureKind.InvalidResponse)
                };
            }
            if (!string.Equals(body.Response, "True", StringComparison.Ordinal)
                || !string.Equals(body.ImdbId, imdbId, StringComparison.Ordinal))
            {
                return Failed(RatingsFailureKind.InvalidResponse);
            }

            return new(
                body.ImdbId,
                ParseImdbRating(body.ImdbRating),
                ParseRottenTomatoesRating(body.Ratings),
                true);
        }
    }

    private static RatingsLookupResult Failed(RatingsFailureKind failure) =>
        new(null, null, null, false, failure);

    private static double? ParseImdbRating(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
        && double.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var rating)
        && rating is >= 0 and <= 10
            ? rating
            : null;

    private static int? ParseRottenTomatoesRating(
        IReadOnlyList<OmdbRatingResponse>? ratings)
    {
        var value = ratings?.FirstOrDefault(rating => string.Equals(
            rating.Source,
            "Rotten Tomatoes",
            StringComparison.Ordinal))?.Value;
        return value is not null
            && value.EndsWith('%')
            && int.TryParse(
                value[..^1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rating)
            && rating is >= 0 and <= 100
                ? rating
                : null;
    }

    private sealed record OmdbResponse
    {
        [JsonPropertyName("Response")]
        public string? Response { get; init; }

        [JsonPropertyName("Error")]
        public string? Error { get; init; }

        [JsonPropertyName("imdbID")]
        public string? ImdbId { get; init; }

        [JsonPropertyName("imdbRating")]
        public string? ImdbRating { get; init; }

        [JsonPropertyName("Ratings")]
        public OmdbRatingResponse[]? Ratings { get; init; }
    }

    private sealed record OmdbRatingResponse
    {
        [JsonPropertyName("Source")]
        public string? Source { get; init; }

        [JsonPropertyName("Value")]
        public string? Value { get; init; }
    }
}
