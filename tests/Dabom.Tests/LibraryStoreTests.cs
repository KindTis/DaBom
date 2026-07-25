using Dabom.Library;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dabom.Tests;

[TestClass]
public sealed class LibraryStoreTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsAllLibraryState()
    {
        var root = Directory.CreateTempSubdirectory("dabom-store-");
        try
        {
            var store = new LibraryStore(root.FullName);
            var path = Path.GetFullPath(Path.Combine(root.FullName, "Movie.mkv"));
            var expected = new LibraryData
            {
                Locations = [root.FullName],
                VideosByPath = new(StringComparer.OrdinalIgnoreCase)
                {
                    [path] = new()
                    {
                        Title = "영화",
                        OriginalTitle = "Original Movie",
                        ReleaseDate = new DateOnly(2026, 7, 18),
                        Director = "감독",
                        Actors = ["배우"],
                        Synopsis = "줄거리",
                        Poster = "posters/poster.jpg",
                        MediaType = MediaType.TvEpisode,
                        SeriesTitle = "도깨비",
                        EpisodeTitle = "검의 주인",
                        SeasonNumber = 1,
                        EpisodeNumber = 4,
                        Genres = ["드라마", "판타지"],
                        MetadataStatus = MetadataStatus.Matched,
                        ProviderReferences =
                        [
                            new("tmdb", "tv-series", "67915"),
                            new("tmdb", "tv-episode", "123456")
                        ],
                        UserEditedFields = [MetadataField.Synopsis, MetadataField.Poster],
                        FileSizeBytes = 10,
                        LastWriteTimeUtc = DateTimeOffset.Parse("2026-07-18T09:00:00Z"),
                        DurationTicks = 20,
                        LastPlayedUtc = DateTimeOffset.Parse("2026-07-18T10:00:00Z")
                    }
                }
            };

            await store.SaveAsync(expected, CancellationToken.None);
            var actual = await store.LoadAsync(CancellationToken.None);

            CollectionAssert.AreEqual(expected.Locations, actual.Locations);
            var expectedVideo = expected.VideosByPath[path];
            var actualVideo = actual.VideosByPath[path];
            Assert.AreEqual(expectedVideo.Title, actualVideo.Title);
            Assert.AreEqual(expectedVideo.OriginalTitle, actualVideo.OriginalTitle);
            Assert.AreEqual(expectedVideo.ReleaseDate, actualVideo.ReleaseDate);
            Assert.AreEqual(expectedVideo.Director, actualVideo.Director);
            CollectionAssert.AreEqual(expectedVideo.Actors, actualVideo.Actors);
            Assert.AreEqual(expectedVideo.Synopsis, actualVideo.Synopsis);
            Assert.AreEqual(expectedVideo.Poster, actualVideo.Poster);
            Assert.AreEqual(expectedVideo.MediaType, actualVideo.MediaType);
            Assert.AreEqual(expectedVideo.SeriesTitle, actualVideo.SeriesTitle);
            Assert.AreEqual(expectedVideo.EpisodeTitle, actualVideo.EpisodeTitle);
            Assert.AreEqual(expectedVideo.SeasonNumber, actualVideo.SeasonNumber);
            Assert.AreEqual(expectedVideo.EpisodeNumber, actualVideo.EpisodeNumber);
            CollectionAssert.AreEqual(expectedVideo.Genres, actualVideo.Genres);
            Assert.AreEqual(expectedVideo.MetadataStatus, actualVideo.MetadataStatus);
            CollectionAssert.AreEqual(
                expectedVideo.ProviderReferences, actualVideo.ProviderReferences);
            CollectionAssert.AreEquivalent(
                expectedVideo.UserEditedFields.ToArray(),
                actualVideo.UserEditedFields.ToArray());
            Assert.AreEqual(expectedVideo.FileSizeBytes, actualVideo.FileSizeBytes);
            Assert.AreEqual(expectedVideo.LastWriteTimeUtc, actualVideo.LastWriteTimeUtc);
            Assert.AreEqual(expectedVideo.DurationTicks, actualVideo.DurationTicks);
            Assert.AreEqual(expectedVideo.LastPlayedUtc, actualVideo.LastPlayedUtc);
            Assert.IsTrue(actual.VideosByPath.Comparer.Equals(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MigratesLegacyManualAndUntouchedRecords()
    {
        var root = Directory.CreateTempSubdirectory("dabom-metadata-migration-");
        try
        {
            var manualPath = Path.Combine(root.FullName, "Manual.mkv");
            var pendingPath = Path.Combine(root.FullName, "Pending.mkv");
            var json = $$"""
            {
              "locations": ["{{root.FullName.Replace("\\", "\\\\")}}"],
              "videosByPath": {
                "{{manualPath.Replace("\\", "\\\\")}}": { "title": "직접 고친 제목", "actors": [] },
                "{{pendingPath.Replace("\\", "\\\\")}}": { "title": "Pending", "actors": [] }
              }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "library.json"), json);

            var actual = await new LibraryStore(root.FullName).LoadAsync(
                CancellationToken.None);

            Assert.AreEqual(
                MetadataStatus.Manual,
                actual.VideosByPath[manualPath].MetadataStatus);
            Assert.AreEqual(
                MetadataStatus.Pending,
                actual.VideosByPath[pendingPath].MetadataStatus);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_NormalizesMissingMetadataCollections()
    {
        var root = Directory.CreateTempSubdirectory("dabom-metadata-null-");
        try
        {
            var path = Path.Combine(root.FullName, "Movie.mkv");
            await File.WriteAllTextAsync(
                Path.Combine(root.FullName, "library.json"),
                $$$"""
                {"locations":[],"videosByPath":{
                  "{{{path.Replace("\\", "\\\\")}}}":{"actors":[]}
                }}
                """);

            var record = (await new LibraryStore(root.FullName).LoadAsync(
                CancellationToken.None)).VideosByPath[path];

            Assert.IsNotNull(record.Genres);
            Assert.IsNotNull(record.ProviderReferences);
            Assert.IsNotNull(record.UserEditedFields);
            Assert.AreNotEqual(MetadataStatus.Unspecified, record.MetadataStatus);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_WhenCommitFails_KeepsExistingJson()
    {
        var root = Directory.CreateTempSubdirectory("dabom-atomic-");
        try
        {
            var working = new LibraryStore(root.FullName);
            await working.SaveAsync(new LibraryData { Locations = [@"D:\Old"] }, CancellationToken.None);
            var failing = new LibraryStore(root.FullName, (_, _, _) => throw new IOException("disk full"));

            await Assert.ThrowsExceptionAsync<IOException>(() =>
                failing.SaveAsync(new LibraryData { Locations = [@"D:\New"] }, CancellationToken.None));

            var reloaded = await working.LoadAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { @"D:\Old" }, reloaded.Locations);
            Assert.AreEqual(0, Directory.GetFiles(root.FullName, "library.*.tmp").Length);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenJsonIsCorrupt_BacksUpAndStartsEmpty()
    {
        var root = Directory.CreateTempSubdirectory("dabom-corrupt-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "library.json"), "{broken");
            var store = new LibraryStore(root.FullName);

            var data = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(0, data.Locations.Length);
            Assert.IsTrue(store.CanSave);
            Assert.IsNotNull(store.LoadWarning);
            Assert.AreEqual(1, Directory.GetFiles(root.FullName, "library.corrupt-*.json").Length);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenExistingJsonCannotBeRead_DisablesSavingAndPreservesFile()
    {
        var root = Directory.CreateTempSubdirectory("dabom-locked-");
        try
        {
            var working = new LibraryStore(root.FullName);
            await working.SaveAsync(new LibraryData { Locations = [@"D:\Movies"] });
            var jsonPath = Path.Combine(root.FullName, "library.json");
            var original = await File.ReadAllBytesAsync(jsonPath);
            var store = new LibraryStore(root.FullName);

            using (new FileStream(jsonPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var data = await store.LoadAsync(CancellationToken.None);

                Assert.AreEqual(0, data.Locations.Length);
                Assert.IsFalse(store.CanSave);
                StringAssert.Contains(store.LoadWarning!, jsonPath);
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                    store.SaveAsync(new LibraryData { Locations = [@"D:\Other"] }));
            }

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(jsonPath));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenJsonDoesNotExist_StartsEmptyWithSavingEnabled()
    {
        var root = Directory.CreateTempSubdirectory("dabom-new-");
        try
        {
            var store = new LibraryStore(root.FullName);

            var data = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(0, data.Locations.Length);
            Assert.IsTrue(store.CanSave);
            Assert.IsNull(store.LoadWarning);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public void ResolvePosterPath_WhenRelativePathIsMalformed_ReturnsNull()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-path-");
        try
        {
            var store = new LibraryStore(root.FullName);

            Assert.IsNull(store.ResolvePosterPath("posters/\0bad.jpg"));
            Assert.IsNull(store.ResolvePosterPath("../outside.jpg"));
            Assert.IsNull(store.ResolvePosterPath(Path.Combine(root.FullName, "outside.jpg")));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ImportPosterAsync_CopiesValidImageAndDeletePosterRemovesIt()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-store-");
        try
        {
            var source = Path.Combine(root.FullName, "source.png");
            WritePng(source);
            var store = new LibraryStore(Path.Combine(root.FullName, "data"));

            var relative = await store.ImportPosterAsync(@"D:\Movie.mkv", source);
            var imported = store.ResolvePosterPath(relative);

            Assert.IsTrue(relative.StartsWith("posters/", StringComparison.Ordinal));
            Assert.IsNotNull(imported);
            Assert.IsTrue(File.Exists(imported));
            store.DeletePoster(relative);
            Assert.IsFalse(File.Exists(imported));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task ImportPosterAsync_WhenExtensionIsUnsupported_RejectsImage()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-extension-");
        try
        {
            var source = Path.Combine(root.FullName, "source.gif");
            await File.WriteAllBytesAsync(source, [1, 2, 3]);
            var store = new LibraryStore(Path.Combine(root.FullName, "data"));

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.ImportPosterAsync(@"D:\Movie.mkv", source));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DownloadPosterAsync_UsesDecodedFormatAndRelativePath()
    {
        var root = Directory.CreateTempSubdirectory("dabom-remote-poster-");
        try
        {
            var store = new LibraryStore(root.FullName);
            using var client = new HttpClient(new ResponseHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(PngBytes())
                }));

            var relative = await store.DownloadPosterAsync(
                client,
                new Uri("https://image.tmdb.org/poster.bin"),
                CancellationToken.None);

            StringAssert.EndsWith(relative, ".png");
            Assert.IsTrue(File.Exists(store.ResolvePosterPath(relative)));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DownloadPosterAsync_AcceptsDecodableJpegWithUnsupportedMetadata()
    {
        var root = Directory.CreateTempSubdirectory("dabom-remote-jpeg-metadata-");
        try
        {
            var store = new LibraryStore(root.FullName);
            using var client = new HttpClient(new ResponseHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(JpegWithUnsupportedMetadata())
                }));

            var relative = await store.DownloadPosterAsync(
                client,
                new Uri("https://image.tmdb.org/poster.jpg"),
                CancellationToken.None);

            StringAssert.EndsWith(relative, ".jpg");
            Assert.IsTrue(File.Exists(store.ResolvePosterPath(relative)));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DownloadPosterAsync_RejectsOversizeAndInvalidImagesWithoutResidue()
    {
        var root = Directory.CreateTempSubdirectory("dabom-remote-invalid-");
        try
        {
            var store = new LibraryStore(root.FullName);
            using var oversize = new HttpClient(new ResponseHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1])
                };
                response.Content.Headers.ContentLength = 10_485_761;
                return response;
            }));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.DownloadPosterAsync(
                    oversize,
                    new Uri("https://image.tmdb.org/large.jpg"),
                    CancellationToken.None));

            using var invalid = new HttpClient(new ResponseHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3])
                }));
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                store.DownloadPosterAsync(
                    invalid,
                    new Uri("https://image.tmdb.org/bad.jpg"),
                    CancellationToken.None));

            Assert.AreEqual(0, Directory.EnumerateFiles(store.PostersPath).Count());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_WhenJsonCommitFails_DeletesCreatedPoster()
    {
        var root = Directory.CreateTempSubdirectory("dabom-poster-rollback-");
        try
        {
            var working = new LibraryStore(root.FullName);
            var source = Path.Combine(root.FullName, "source.png");
            await File.WriteAllBytesAsync(source, PngBytes());
            var created = await working.ImportPosterAsync(@"D:\Movie.mkv", source);
            var failing = new LibraryStore(
                root.FullName,
                (_, _, _) => throw new IOException("disk full"));

            await Assert.ThrowsExceptionAsync<IOException>(() =>
                failing.SaveAsync(
                    new LibraryData(),
                    created,
                    CancellationToken.None));

            Assert.IsFalse(File.Exists(working.ResolvePosterPath(created)));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DownloadPosterAsync_WhenCanceledDuringRead_RemovesTemporaryFile()
    {
        var root = Directory.CreateTempSubdirectory("dabom-remote-cancel-");
        try
        {
            var store = new LibraryStore(root.FullName);
            var stream = new BlockingReadStream();
            using var client = new HttpClient(new ResponseHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(stream)
                }));
            using var cancellation = new CancellationTokenSource();

            var download = store.DownloadPosterAsync(
                client,
                new Uri("https://image.tmdb.org/slow.jpg"),
                cancellation.Token);
            await stream.Blocked.Task;
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => download);
            Assert.AreEqual(0, Directory.EnumerateFiles(store.PostersPath).Count());
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task DownloadPosterAsync_WhenCanceledDuringValidation_RemovesTemporaryFile()
    {
        var root = Directory.CreateTempSubdirectory("dabom-remote-validation-cancel-");
        try
        {
            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new LibraryStore(
                root.FullName,
                getPosterExtension: async (_, token) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return ".png";
                });
            using var client = new HttpClient(new ResponseHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(PngBytes())
                }));
            using var cancellation = new CancellationTokenSource();

            var download = store.DownloadPosterAsync(
                client,
                new Uri("https://image.tmdb.org/poster.png"),
                cancellation.Token);
            await started.Task;
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => download);
            Assert.AreEqual(0, Directory.EnumerateFiles(store.PostersPath).Count());
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static byte[] PngBytes()
    {
        var bitmap = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] JpegWithUnsupportedMetadata()
    {
        var bitmap = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var jpeg = stream.ToArray();
        var app13 = Convert.FromBase64String(
            "/+0AK1Bob3Rvc2hvcCAzLjAAOEJJTQQEAAAAAAAPHAFaAAMbJUccAgAAAgAA");
        var result = new byte[jpeg.Length + app13.Length];
        Array.Copy(jpeg, 0, result, 0, 20);
        Array.Copy(app13, 0, result, 20, app13.Length);
        Array.Copy(jpeg, 20, result, 20 + app13.Length, jpeg.Length - 20);
        return result;
    }

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(
            4, 6, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 6 * 4], 4 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class BlockingReadStream : Stream
    {
        private bool _sentFirstByte;
        public TaskCompletionSource Blocked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_sentFirstByte)
            {
                _sentFirstByte = true;
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }

            Blocked.TrySetResult();
            return new(WaitForCancellationAsync(cancellationToken));
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private static async Task<int> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
