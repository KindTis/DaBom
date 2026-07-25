using Dabom.Library;
using System.IO;
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

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(
            4, 6, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 6 * 4], 4 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
