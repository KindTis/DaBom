using Dabom.Metadata;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Dabom.Tests;

[TestClass]
public sealed class OmdbRatingsClientTests
{
    [TestMethod]
    public void ReadApiKey_DistinguishesMissingFromUnreadableConfiguration()
    {
        using var client = new HttpClient(new RecordingHandler(_ => Json("{}")));
        var missing = new OmdbRatingsClient(client, () => null).ReadApiKey();
        var unreadable = new OmdbRatingsClient(
            client,
            () => throw new IOException("denied")).ReadApiKey();

        Assert.IsNull(missing.ApiKey);
        Assert.AreEqual(RatingsFailureKind.MissingKey, missing.Failure);
        Assert.IsNull(unreadable.ApiKey);
        Assert.AreEqual(RatingsFailureKind.Configuration, unreadable.Failure);
    }

    [TestMethod]
    public void ReadApiKey_ReportsConfigurationWhenEnvPathCannotBeRead()
    {
        var root = Directory.CreateTempSubdirectory("dabom-omdb-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Dabom", ".env"));
            using var http = new HttpClient(new RecordingHandler(_ => Json("{}")));
            var client = new OmdbRatingsClient(
                http,
                () => LocalEnvironment.ReadFromLocalApplicationData(
                    root.FullName,
                    "DABOM_OMDB_API_KEY"));

            var result = client.ReadApiKey();

            Assert.IsNull(result.ApiKey);
            Assert.AreEqual(RatingsFailureKind.Configuration, result.Failure);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task FetchAsync_ParsesValidRatingsAndDropsOnlyInvalidValue()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json("""
                {"Response":"True","imdbID":"tt1234567","imdbRating":"8.7",
                 "Ratings":[{"Source":"Rotten Tomatoes","Value":"83%"}]}
                """),
            Json("""
                {"Response":"True","imdbID":"tt1234567","imdbRating":"11.2",
                 "Ratings":[{"Source":"Rotten Tomatoes","Value":"83%"}]}
                """)
        ]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        using var http = new HttpClient(handler);
        var client = new OmdbRatingsClient(http, () => "secret-key");

        var valid = await client.FetchAsync(
            "secret-key", "tt1234567", CancellationToken.None);
        var partial = await client.FetchAsync(
            "secret-key", "tt1234567", CancellationToken.None);

        Assert.AreEqual(8.7, valid.ImdbRating);
        Assert.AreEqual(83, valid.RottenTomatoesRating);
        Assert.IsTrue(valid.Fetched);
        Assert.IsNull(partial.ImdbRating);
        Assert.AreEqual(83, partial.RottenTomatoesRating);
        Assert.IsTrue(partial.Fetched);
        Assert.IsTrue(handler.Requests.All(uri =>
            uri.Query.Contains("i=tt1234567", StringComparison.Ordinal)));
        Assert.IsTrue(handler.Requests.All(uri =>
            uri.Query.Contains("apikey=secret-key", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task FetchAsync_ClassifiesCompletionRetryAndInvalidResponses()
    {
        var cases = new (HttpResponseMessage Response, bool Fetched, RatingsFailureKind? Failure)[]
        {
            (Json("""{"Response":"True","imdbID":"tt1234567","imdbRating":"N/A","Ratings":[{"Source":"Rotten Tomatoes","Value":"N/A"}]}"""), true, null),
            (Json("""{"Response":"False","Error":"Movie not found!"}"""), true, null),
            (Json("""{"Response":"False","Error":"Incorrect IMDb ID."}"""), true, null),
            (Json("{}", HttpStatusCode.Unauthorized), false, RatingsFailureKind.Authentication),
            (Json("{}", HttpStatusCode.Forbidden), false, RatingsFailureKind.Authentication),
            (Json("""{"Response":"False","Error":"Invalid API key!"}"""), false, RatingsFailureKind.Authentication),
            (Json("{}", HttpStatusCode.TooManyRequests), false, RatingsFailureKind.RateLimited),
            (Json("""{"Response":"False","Error":"Request limit reached!"}"""), false, RatingsFailureKind.RateLimited),
            (Json("{}", HttpStatusCode.BadGateway), false, RatingsFailureKind.Transient),
            (Json("{broken"), false, RatingsFailureKind.InvalidResponse),
            (Json("""{"imdbID":"tt1234567"}"""), false, RatingsFailureKind.InvalidResponse),
            (Json("""{"Response":"True","imdbID":"tt7654321"}"""), false, RatingsFailureKind.InvalidResponse),
            (Json("""{"Response":"False","Error":"Something unexpected"}"""), false, RatingsFailureKind.InvalidResponse)
        };

        foreach (var @case in cases)
        {
            var handler = new RecordingHandler(_ => @case.Response);
            using var http = new HttpClient(handler);
            var client = new OmdbRatingsClient(http, () => "secret-key");

            var result = await client.FetchAsync(
                "secret-key", "tt1234567", CancellationToken.None);

            Assert.AreEqual(@case.Fetched, result.Fetched);
            Assert.AreEqual(@case.Failure, result.Failure);
            Assert.IsFalse(result.ToString().Contains("secret-key", StringComparison.Ordinal));
        }

        using var errorHttp = new HttpClient(new RecordingHandler(_ =>
            throw new HttpRequestException("secret-key")));
        var errorClient = new OmdbRatingsClient(errorHttp, () => "secret-key");
        var transient = await errorClient.FetchAsync(
            "secret-key", "tt1234567", CancellationToken.None);

        Assert.IsFalse(transient.Fetched);
        Assert.AreEqual(RatingsFailureKind.Transient, transient.Failure);
        Assert.IsFalse(transient.ToString().Contains("secret-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FetchAsync_RejectsInvalidImdbIdsBeforeRequest()
    {
        foreach (var imdbId in new[] { "foo", "", "tt12x" })
        {
            var handler = new RecordingHandler(_ => Json("{}"));
            using var http = new HttpClient(handler);
            var client = new OmdbRatingsClient(http, () => "secret-key");

            var result = await client.FetchAsync(
                "secret-key", imdbId, CancellationToken.None);

            Assert.IsFalse(result.Fetched);
            Assert.AreEqual(RatingsFailureKind.InvalidResponse, result.Failure);
            Assert.AreEqual(0, handler.Requests.Count);
        }
    }

    [TestMethod]
    public async Task FetchAsync_ClassifiesResponseStreamNetworkErrorsAsTransient()
    {
        foreach (Func<Exception> createError in
            new Func<Exception>[]
            {
                () => new HttpRequestException("network failed"),
                () => new IOException("network failed")
            })
        {
            var handler = new RecordingHandler(_ => new(HttpStatusCode.OK)
            {
                Content = new ThrowingContent(createError)
            });
            using var http = new HttpClient(handler);
            var client = new OmdbRatingsClient(http, () => "secret-key");

            var result = await client.FetchAsync(
                "secret-key", "tt1234567", CancellationToken.None);

            Assert.IsFalse(result.Fetched);
            Assert.AreEqual(RatingsFailureKind.Transient, result.Failure);
        }
    }

    [TestMethod]
    public async Task FetchAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new RecordingHandler((_, token) =>
            Task.FromCanceled<HttpResponseMessage>(token)));
        var client = new OmdbRatingsClient(http, () => "secret-key");
        cancellation.Cancel();

        try
        {
            await client.FetchAsync("secret-key", "tt1234567", cancellation.Token);
            Assert.Fail("취소가 결과로 변환되면 안 됩니다.");
        }
        catch (OperationCanceledException error)
        {
            Assert.IsFalse(error.Message.Contains(
                "secret-key",
                StringComparison.Ordinal));
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
            _respond = (request, _) => Task.FromResult(respond(request));

        internal RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            : this(_ => Json("{}"))
        {
            _respond = respond;
        }

        internal List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return _respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ThrowingContent(Func<Exception> createError) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(createError());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromException<Stream>(createError());

        protected override Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<Stream>(createError());
    }
}
