using Dabom.Library;
using Dabom.Main;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace Dabom.Metadata;

public sealed class MetadataEditorViewModel : ViewModelBase
{
    private readonly Func<MetadataEditorViewModel, CancellationToken, Task<string?>> _commit;
    private readonly Func<string, CancellationToken,
        Task<IReadOnlyList<MetadataCandidate>>>? _search;
    private readonly Func<MetadataCandidate, CancellationToken,
        Task<MetadataDetails>>? _getDetails;
    private readonly CancellationTokenSource _lookupCancellation = new();
    private IReadOnlyList<MetadataCandidate> _searchCandidates = [];
    private MetadataCandidate? _pendingTvCandidate;
    private VideoRecord? _selectedBaseline;
    private Uri? _selectedPosterUri;
    private string _searchText;
    private string _seasonNumberText = string.Empty;
    private string _episodeNumberText = string.Empty;
    private string _title = string.Empty;
    private string _originalTitle = string.Empty;
    private DateTime? _releaseDate;
    private string _director = string.Empty;
    private string _actorsText = string.Empty;
    private string _synopsis = string.Empty;
    private string? _errorMessage;
    private object? _previewPoster;
    private bool _isLookupInProgress;
    private bool _isSearchPopupOpen;
    private bool _isSaving;

    internal MetadataEditorViewModel(
        string path,
        VideoRecord record,
        BitmapSource? currentPoster,
        Func<MetadataEditorViewModel, CancellationToken, Task<string?>> commit,
        Func<string, CancellationToken,
            Task<IReadOnlyList<MetadataCandidate>>>? search = null,
        Func<MetadataCandidate, CancellationToken,
            Task<MetadataDetails>>? getDetails = null)
    {
        Path = path;
        OriginalRecord = record;
        _previewPoster = currentPoster;
        _commit = commit;
        _search = search;
        _getDetails = getDetails;
        _title = record.Title ?? string.Empty;
        _originalTitle = record.OriginalTitle ?? string.Empty;
        _releaseDate = record.ReleaseDate is DateOnly date
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        _director = record.Director ?? string.Empty;
        _actorsText = string.Join(", ", record.Actors);
        _synopsis = record.Synopsis ?? string.Empty;
        _searchText = string.IsNullOrWhiteSpace(record.Title)
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : record.Title;
    }

    public string Path { get; }
    internal VideoRecord OriginalRecord { get; }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string OriginalTitle
    {
        get => _originalTitle;
        set => Set(ref _originalTitle, value);
    }

    public DateTime? ReleaseDate
    {
        get => _releaseDate;
        set => Set(ref _releaseDate, value);
    }

    public string Director
    {
        get => _director;
        set => Set(ref _director, value);
    }

    public string ActorsText
    {
        get => _actorsText;
        set => Set(ref _actorsText, value);
    }

    public string Synopsis
    {
        get => _synopsis;
        set => Set(ref _synopsis, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public IReadOnlyList<MetadataCandidate> SearchCandidates
    {
        get => _searchCandidates;
        private set => Set(ref _searchCandidates, value);
    }

    public MetadataCandidate? PendingTvCandidate
    {
        get => _pendingTvCandidate;
        private set
        {
            if (Set(ref _pendingTvCandidate, value))
            {
                Raise(nameof(CanApplyTvEpisode));
            }
        }
    }

    public string SeasonNumberText
    {
        get => _seasonNumberText;
        set
        {
            if (Set(ref _seasonNumberText, value))
            {
                Raise(nameof(CanApplyTvEpisode));
            }
        }
    }

    public string EpisodeNumberText
    {
        get => _episodeNumberText;
        set
        {
            if (Set(ref _episodeNumberText, value))
            {
                Raise(nameof(CanApplyTvEpisode));
            }
        }
    }

    public bool IsLookupInProgress
    {
        get => _isLookupInProgress;
        private set
        {
            if (Set(ref _isLookupInProgress, value))
            {
                Raise(nameof(IsNotBusy));
            }
        }
    }

    public bool IsSearchPopupOpen
    {
        get => _isSearchPopupOpen;
        set => Set(ref _isSearchPopupOpen, value);
    }

    public string? SelectedPosterSourcePath { get; set; }
    public bool RemovePoster { get; set; }
    internal bool HasSelectedResult => _selectedBaseline is not null;
    internal Uri? SelectedPosterUri
    {
        get => _selectedPosterUri;
        private set => _selectedPosterUri = value;
    }

    public bool CanApplyTvEpisode =>
        PendingTvCandidate is not null
        && TryPositiveNumber(SeasonNumberText, out _)
        && TryPositiveNumber(EpisodeNumberText, out _);

    public bool IsNotBusy => !IsSaving && !IsLookupInProgress;
    public string FileSizeText => $"{OriginalRecord.FileSizeBytes / 1024d / 1024d:N1} MB";
    public string ModifiedText => OriginalRecord.LastWriteTimeUtc
        .ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string DurationText => LibraryRules.DurationText(OriginalRecord.DurationTicks);

    public object? PreviewPoster
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
            if (Set(ref _isSaving, value))
            {
                Raise(nameof(IsNotSaving));
                Raise(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotSaving => !IsSaving;

    public async Task<bool> SearchAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ErrorMessage = "검색할 작품명을 입력하세요.";
            return false;
        }
        if (_search is null)
        {
            ErrorMessage = LookupError(
                MetadataProviderFailureKind.InvalidResponse);
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lookupCancellation.Token);
        IsLookupInProgress = true;
        try
        {
            var candidates = await _search(SearchText.Trim(), linked.Token);
            SearchCandidates = candidates;
            PendingTvCandidate = null;
            SeasonNumberText = string.Empty;
            EpisodeNumberText = string.Empty;
            IsSearchPopupOpen = true;
            ErrorMessage = candidates.Count == 0
                ? "검색 결과가 없습니다"
                : null;
            return true;
        }
        catch (MetadataProviderException error)
        {
            ErrorMessage = LookupError(error.Kind);
            return false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsLookupInProgress = false;
        }
    }

    public async Task<bool> SelectCandidateAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress)
        {
            return false;
        }
        if (candidate.MediaType == MediaType.TvEpisode)
        {
            PendingTvCandidate = candidate;
            SeasonNumberText = PositiveNumberText(OriginalRecord.SeasonNumber);
            EpisodeNumberText = PositiveNumberText(OriginalRecord.EpisodeNumber);
            ErrorMessage = null;
            return false;
        }

        return await LoadDetailsAsync(candidate, cancellationToken);
    }

    public async Task<bool> ApplyTvEpisodeAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress)
        {
            return false;
        }
        if (PendingTvCandidate is not { } pending
            || !TryPositiveNumber(SeasonNumberText, out var seasonNumber)
            || !TryPositiveNumber(EpisodeNumberText, out var episodeNumber))
        {
            ErrorMessage =
                "시즌과 에피소드 번호에 1 이상의 정수를 입력하세요.";
            return false;
        }

        var candidate = pending with
        {
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber
        };
        return await LoadDetailsAsync(candidate, cancellationToken);
    }

    public async Task<bool> SaveAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress)
        {
            return false;
        }
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

    public void CancelLookup() => _lookupCancellation.Cancel();

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

    private async Task<bool> LoadDetailsAsync(
        MetadataCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (_getDetails is null)
        {
            ErrorMessage = LookupError(
                MetadataProviderFailureKind.InvalidResponse);
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lookupCancellation.Token);
        IsLookupInProgress = true;
        try
        {
            ApplyDetails(await _getDetails(candidate, linked.Token));
            return true;
        }
        catch (MetadataProviderException error)
        {
            ErrorMessage = LookupError(error.Kind);
            return false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsLookupInProgress = false;
        }
    }

    private void ApplyDetails(MetadataDetails details)
    {
        _selectedBaseline = OriginalRecord with
        {
            Title = details.MediaType == MediaType.TvEpisode
                ? MetadataEnrichmentService.BuildEpisodeTitle(
                    details.SeriesTitle,
                    details.EpisodeTitle,
                    details.SeasonNumber,
                    details.EpisodeNumber)
                : details.Title,
            OriginalTitle = details.OriginalTitle,
            ReleaseDate = details.ReleaseDate,
            Director = details.Director,
            Actors = details.Actors,
            Synopsis = details.Synopsis,
            Poster = null,
            MediaType = details.MediaType,
            SeriesTitle = details.SeriesTitle,
            EpisodeTitle = details.EpisodeTitle,
            SeasonNumber = details.SeasonNumber,
            EpisodeNumber = details.EpisodeNumber,
            Genres = details.Genres,
            MetadataStatus = MetadataStatus.Matched,
            ProviderReferences = details.ProviderReferences,
            UserEditedFields = []
        };

        Title = _selectedBaseline.Title ?? string.Empty;
        OriginalTitle = _selectedBaseline.OriginalTitle ?? string.Empty;
        ReleaseDate = _selectedBaseline.ReleaseDate is DateOnly date
            ? date.ToDateTime(TimeOnly.MinValue)
            : null;
        Director = _selectedBaseline.Director ?? string.Empty;
        ActorsText = string.Join(", ", _selectedBaseline.Actors);
        Synopsis = _selectedBaseline.Synopsis ?? string.Empty;
        SelectedPosterSourcePath = null;
        RemovePoster = false;
        SelectedPosterUri = details.PosterUri;
        PreviewPoster = details.PosterUri;
        PendingTvCandidate = null;
        IsSearchPopupOpen = false;
        ErrorMessage = null;
    }

    private static bool TryPositiveNumber(string value, out int number) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number)
        && number > 0;

    private static string PositiveNumberText(int? value) =>
        value is > 0
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    private static string LookupError(MetadataProviderFailureKind kind) =>
        kind switch
        {
            MetadataProviderFailureKind.Authentication =>
                ".env의 DABOM_TMDB_ACCESS_TOKEN을 확인하세요.",
            MetadataProviderFailureKind.Transient =>
                "온라인 메타데이터 조회에 실패했습니다. 잠시 후 다시 시도하세요.",
            _ => "메타데이터 응답을 처리하지 못했습니다. 다시 시도하세요."
        };
}
