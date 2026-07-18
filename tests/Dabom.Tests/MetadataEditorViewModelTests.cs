using Dabom.Library;
using Dabom.Metadata;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dabom.Tests;

[TestClass]
public sealed class MetadataEditorViewModelTests
{
    [TestMethod]
    public async Task SaveAsync_WhenCommitFails_KeepsDraftOpenForRetry()
    {
        var root = Directory.CreateTempSubdirectory("dabom-editor-failure-");
        try
        {
            var posterPath = Path.Combine(root.FullName, "new.png");
            WritePng(posterPath);
            var record = new VideoRecord { Title = "이전 제목", Actors = ["배우 A"] };
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv", record, null,
                (_, _) => Task.FromResult<string?>("저장 공간 부족"));
            editor.Title = "새 제목";
            editor.ActorsText = "배우 A, 배우 B";
            editor.ChoosePoster(posterPath);
            var preview = editor.PreviewPoster;

            var saved = await editor.SaveAsync();

            Assert.IsFalse(saved);
            Assert.AreEqual("새 제목", editor.Title);
            Assert.AreEqual("배우 A, 배우 B", editor.ActorsText);
            Assert.AreEqual(posterPath, editor.SelectedPosterSourcePath);
            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.AreEqual("저장 공간 부족", editor.ErrorMessage);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_RejectsConcurrentSaveUntilFirstCompletes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commits = 0;
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv", new VideoRecord(), null,
            async (_, _) =>
            {
                commits++;
                started.TrySetResult();
                await release.Task;
                return null;
            });

        var first = editor.SaveAsync();
        await started.Task;

        Assert.IsTrue(editor.IsSaving);
        Assert.IsFalse(await editor.SaveAsync());
        Assert.AreEqual(1, commits);

        release.TrySetResult();
        Assert.IsTrue(await first);
        Assert.IsFalse(editor.IsSaving);
    }

    [TestMethod]
    public void Constructor_ExposesDraftAndFileInformation()
    {
        var preview = BitmapSource.Create(
            1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        preview.Freeze();
        var modified = DateTimeOffset.Parse("2026-07-18T10:30:00+09:00");
        var editor = new MetadataEditorViewModel(
            @"D:\Movie.mkv",
            new VideoRecord
            {
                Title = "제목",
                OriginalTitle = "Original",
                ReleaseDate = new DateOnly(2026, 7, 18),
                Director = "감독",
                Actors = ["배우 A", "배우 B"],
                Synopsis = "줄거리",
                FileSizeBytes = 1572864,
                LastWriteTimeUtc = modified,
                DurationTicks = TimeSpan.FromMinutes(91).Ticks
            },
            preview,
            (_, _) => Task.FromResult<string?>(null));

        Assert.AreEqual("제목", editor.Title);
        Assert.AreEqual("Original", editor.OriginalTitle);
        Assert.AreEqual(new DateTime(2026, 7, 18), editor.ReleaseDate);
        Assert.AreEqual("감독", editor.Director);
        Assert.AreEqual("배우 A, 배우 B", editor.ActorsText);
        Assert.AreEqual("줄거리", editor.Synopsis);
        Assert.AreEqual("1.5 MB", editor.FileSizeText);
        Assert.AreEqual(modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), editor.ModifiedText);
        Assert.AreEqual("1시간 31분", editor.DurationText);
        Assert.AreSame(preview, editor.PreviewPoster);
        Assert.IsTrue(editor.HasPreviewPoster);
    }

    [TestMethod]
    public async Task ChoosePoster_WhenImageIsInvalid_KeepsCurrentPreview()
    {
        var root = Directory.CreateTempSubdirectory("dabom-editor-invalid-poster-");
        try
        {
            var invalidPath = Path.Combine(root.FullName, "invalid.png");
            await File.WriteAllTextAsync(invalidPath, "not an image");
            var preview = BitmapSource.Create(
                1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            preview.Freeze();
            var editor = new MetadataEditorViewModel(
                @"D:\Movie.mkv", new VideoRecord(), preview,
                (_, _) => Task.FromResult<string?>(null));

            editor.ChoosePoster(invalidPath);

            Assert.AreSame(preview, editor.PreviewPoster);
            Assert.IsNull(editor.SelectedPosterSourcePath);
            StringAssert.Contains(editor.ErrorMessage, "JPG");
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
