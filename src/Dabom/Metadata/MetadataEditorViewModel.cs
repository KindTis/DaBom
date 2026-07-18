using Dabom.Library;
using Dabom.Main;
using System.Windows.Media.Imaging;

namespace Dabom.Metadata;

public sealed class MetadataEditorViewModel : ViewModelBase
{
    private readonly Func<MetadataEditorViewModel, CancellationToken, Task<string?>> _commit;
    private string? _errorMessage;
    private BitmapSource? _previewPoster;
    private bool _isSaving;

    internal MetadataEditorViewModel(
        string path,
        VideoRecord record,
        BitmapSource? currentPoster,
        Func<MetadataEditorViewModel, CancellationToken, Task<string?>> commit)
    {
        Path = path;
        OriginalRecord = record;
        _previewPoster = currentPoster;
        _commit = commit;
        Title = record.Title ?? string.Empty;
        OriginalTitle = record.OriginalTitle ?? string.Empty;
        ReleaseDate = record.ReleaseDate is DateOnly date
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        Director = record.Director ?? string.Empty;
        ActorsText = string.Join(", ", record.Actors);
        Synopsis = record.Synopsis ?? string.Empty;
    }

    public string Path { get; }
    internal VideoRecord OriginalRecord { get; }
    public string Title { get; set; }
    public string OriginalTitle { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Director { get; set; }
    public string ActorsText { get; set; }
    public string Synopsis { get; set; }
    public string? SelectedPosterSourcePath { get; set; }
    public bool RemovePoster { get; set; }
    public string FileSizeText => $"{OriginalRecord.FileSizeBytes / 1024d / 1024d:N1} MB";
    public string ModifiedText => OriginalRecord.LastWriteTimeUtc
        .ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string DurationText => LibraryRules.DurationText(OriginalRecord.DurationTicks);

    public BitmapSource? PreviewPoster
    {
        get => _previewPoster;
        private set
        {
            if (Set(ref _previewPoster, value)) Raise(nameof(HasPreviewPoster));
        }
    }

    public bool HasPreviewPoster => PreviewPoster is not null;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (Set(ref _isSaving, value)) Raise(nameof(IsNotSaving));
        }
    }

    public bool IsNotSaving => !IsSaving;

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsSaving) return false;
        IsSaving = true;
        try
        {
            ErrorMessage = await _commit(this, cancellationToken);
            return ErrorMessage is null;
        }
        finally
        {
            IsSaving = false;
        }
    }

    internal string[] ParsedActors() => ActorsText.Split(
        ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public void ChoosePoster(string path)
    {
        var preview = PosterImage.TryLoad(path);
        if (preview is null)
        {
            ErrorMessage = "JPG, JPEG, PNG 또는 BMP 이미지 파일을 선택하세요.";
            return;
        }

        SelectedPosterSourcePath = path;
        RemovePoster = false;
        PreviewPoster = preview;
        ErrorMessage = null;
    }

    public void MarkPosterRemoved()
    {
        SelectedPosterSourcePath = null;
        RemovePoster = true;
        PreviewPoster = null;
        ErrorMessage = null;
    }
}
