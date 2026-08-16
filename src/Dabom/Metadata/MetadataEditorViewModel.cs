using Dabom.Library;
using Dabom.Main;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Dabom.Metadata;

public sealed class MetadataEditorViewModel : ViewModelBase
{
    private readonly Func<MetadataEditorViewModel, CancellationToken, Task<string?>> _commit;
    private readonly Func<string, CancellationToken,
        Task<IReadOnlyList<MetadataCandidate>>>? _search;
    private readonly Func<MetadataCandidate, CancellationToken,
        Task<MetadataDetails>>? _getDetails;
    private readonly Func<MetadataCandidate, CancellationToken,
        Task<IReadOnlyList<TvSeasonCandidate>>>? _getTvSeasons;
    private readonly Func<MetadataCandidate, int, CancellationToken,
        Task<IReadOnlyList<TvEpisodeCandidate>>>? _getTvEpisodes;
    private readonly CancellationTokenSource _lookupCancellation = new();
    private IReadOnlyList<MetadataCandidate> _searchCandidates = [];
    private IReadOnlyList<TvSeasonCandidate> _tvSeasons = [];
    private IReadOnlyList<TvEpisodeCandidate> _tvEpisodes = [];
    private MetadataCandidate? _pendingTvCandidate;
    private TvSeasonCandidate? _selectedTvSeason;
    private LookupStep _lookupStep;
    private int? _lookupHintSeason;
    private int? _lookupHintEpisode;
    private string? _lookupHintText;
    private VideoRecord? _selectedBaseline;
    private Uri? _selectedPosterUri;
    private string _searchText;
    private string _seasonNumberText = string.Empty;
    private string _episodeNumberText = string.Empty;
    private MediaType _mediaType;
    private string _seriesTitle = string.Empty;
    private string _episodeTitle = string.Empty;
    private string? _tvValidationMessage;
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
            Task<MetadataDetails>>? getDetails = null,
        Func<MetadataCandidate, CancellationToken,
            Task<IReadOnlyList<TvSeasonCandidate>>>? getTvSeasons = null,
        Func<MetadataCandidate, int, CancellationToken,
            Task<IReadOnlyList<TvEpisodeCandidate>>>? getTvEpisodes = null)
    {
        Path = path;
        OriginalRecord = record;
        _previewPoster = currentPoster;
        _commit = commit;
        _search = search;
        _getDetails = getDetails;
        _getTvSeasons = getTvSeasons;
        _getTvEpisodes = getTvEpisodes;
        _mediaType = record.MediaType;
        _seriesTitle = record.SeriesTitle ?? string.Empty;
        _episodeTitle = record.EpisodeTitle ?? string.Empty;
        _seasonNumberText = NumberText(record.SeasonNumber);
        _episodeNumberText = NumberText(record.EpisodeNumber);
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

    public bool IsTvEpisode => _mediaType == MediaType.TvEpisode;

    public string SeriesTitle
    {
        get => _seriesTitle;
        set
        {
            if (Set(ref _seriesTitle, value)) RegenerateEpisodeTitle();
        }
    }

    public string EpisodeTitle
    {
        get => _episodeTitle;
        set
        {
            if (Set(ref _episodeTitle, value)) RegenerateEpisodeTitle();
        }
    }

    public string? TvValidationMessage
    {
        get => _tvValidationMessage;
        private set => Set(ref _tvValidationMessage, value);
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

    public IReadOnlyList<TvSeasonCandidate> TvSeasons
    {
        get => _tvSeasons;
        private set => Set(ref _tvSeasons, value);
    }

    public IReadOnlyList<TvEpisodeCandidate> TvEpisodes
    {
        get => _tvEpisodes;
        private set => Set(ref _tvEpisodes, value);
    }

    public TvSeasonCandidate? SelectedTvSeason
    {
        get => _selectedTvSeason;
        private set => Set(ref _selectedTvSeason, value);
    }

    public bool IsSearchResultStep => _lookupStep == LookupStep.SearchResult;
    public bool IsSeasonStep => _lookupStep == LookupStep.Season;
    public bool IsEpisodeStep => _lookupStep == LookupStep.Episode;

    public string? LookupHintText
    {
        get => _lookupHintText;
        private set => Set(ref _lookupHintText, value);
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
                RegenerateEpisodeTitle();
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
                RegenerateEpisodeTitle();
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
        && TryNonNegativeNumber(SeasonNumberText, out _)
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
            var searchText = SearchText.Trim();
            var hint = LookupHintPattern.Match(searchText);
            var hintSeason = 0;
            var hintEpisode = 0;
            var hasHint = hint.Success
                && int.TryParse(
                    hint.Groups["season"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out hintSeason)
                && int.TryParse(
                    hint.Groups["episode"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out hintEpisode);
            var title = hasHint
                ? hint.Groups["title"].Value.TrimEnd()
                : searchText;
            var candidates = await _search(title, linked.Token);
            SearchCandidates = candidates;
            PendingTvCandidate = null;
            TvSeasons = [];
            TvEpisodes = [];
            SelectedTvSeason = null;
            _lookupHintSeason = hasHint ? hintSeason : null;
            _lookupHintEpisode = hasHint ? hintEpisode : null;
            LookupHintText = hasHint
                ? $"조회할 회차: S{_lookupHintSeason:00}E{_lookupHintEpisode:00}"
                : null;
            SetLookupStep(LookupStep.SearchResult);
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
            if (_getTvSeasons is null)
            {
                PendingTvCandidate = candidate;
                ErrorMessage = null;
                SetLookupStep(LookupStep.Season);
                return false;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lookupCancellation.Token);
            IsLookupInProgress = true;
            try
            {
                var seasons = await _getTvSeasons(candidate, linked.Token);
                PendingTvCandidate = candidate;
                TvSeasons = seasons;
                TvEpisodes = [];
                SelectedTvSeason = null;
                ErrorMessage = null;
                SetLookupStep(LookupStep.Season);

                var season = _lookupHintSeason is int seasonNumber
                    ? seasons.FirstOrDefault(value => value.SeasonNumber == seasonNumber)
                    : null;
                if (season is null || _getTvEpisodes is null)
                {
                    return false;
                }

                SelectedTvSeason = season;
                var episodes = await _getTvEpisodes(
                    candidate,
                    season.SeasonNumber,
                    linked.Token);
                TvEpisodes = episodes;
                SetLookupStep(LookupStep.Episode);
                var episode = _lookupHintEpisode is int episodeNumber
                    ? episodes.FirstOrDefault(value => value.EpisodeNumber == episodeNumber)
                    : null;
                if (episode is null || _getDetails is null)
                {
                    return false;
                }

                return ApplyDetails(await _getDetails(
                    candidate with
                    {
                        SeasonNumber = season.SeasonNumber,
                        EpisodeNumber = episode.EpisodeNumber
                    },
                    linked.Token));
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
            || !TryNonNegativeNumber(SeasonNumberText, out var seasonNumber)
            || !TryPositiveNumber(EpisodeNumberText, out var episodeNumber))
        {
            ErrorMessage =
                "시즌 번호에는 0 이상, 에피소드 번호에는 1 이상의 정수를 입력하세요.";
            return false;
        }

        var candidate = pending with
        {
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber
        };
        return await LoadDetailsAsync(candidate, cancellationToken);
    }

    public async Task<bool> SelectSeasonAsync(
        TvSeasonCandidate season,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress
            || PendingTvCandidate is not { } pending
            || _getTvEpisodes is null)
        {
            return false;
        }

        var selected = TvSeasons.FirstOrDefault(
            value => value.SeasonNumber == season.SeasonNumber);
        if (selected is null)
        {
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lookupCancellation.Token);
        IsLookupInProgress = true;
        try
        {
            SelectedTvSeason = selected;
            var episodes = await _getTvEpisodes(
                pending,
                selected.SeasonNumber,
                linked.Token);
            TvEpisodes = episodes;
            ErrorMessage = null;
            SetLookupStep(LookupStep.Episode);
            return false;
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

    public Task<bool> SelectEpisodeAsync(
        TvEpisodeCandidate episode,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress
            || PendingTvCandidate is not { } pending
            || SelectedTvSeason is not { } season
            || !TvEpisodes.Any(value => value.EpisodeNumber == episode.EpisodeNumber))
        {
            return Task.FromResult(false);
        }

        return LoadDetailsAsync(
            pending with
            {
                SeasonNumber = season.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber
            },
            cancellationToken);
    }

    public void GoBackInLookup()
    {
        if (IsLookupInProgress)
        {
            return;
        }

        if (IsEpisodeStep)
        {
            SelectedTvSeason = null;
            TvEpisodes = [];
            SetLookupStep(LookupStep.Season);
        }
        else if (IsSeasonStep)
        {
            PendingTvCandidate = null;
            TvSeasons = [];
            SetLookupStep(LookupStep.SearchResult);
        }
    }

    public async Task<bool> SaveAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsSaving || IsLookupInProgress)
        {
            return false;
        }
        if (IsTvEpisode
            && !TryReadTvDraft(out _, out _, out _))
        {
            TvValidationMessage =
                "시리즈명과 시즌 번호(0 이상), 에피소드 번호(1 이상)를 입력하세요.";
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

    internal VideoRecord BuildRecord(string? poster)
    {
        var baseline = _selectedBaseline ?? OriginalRecord;
        var edited = _selectedBaseline is null
            ? new HashSet<MetadataField>(OriginalRecord.UserEditedFields)
            : [];
        var title = NullIfWhiteSpace(Title);
        var originalTitle = NullIfWhiteSpace(OriginalTitle);
        DateOnly? releaseDate = ReleaseDate is DateTime date
            ? DateOnly.FromDateTime(date)
            : null;
        var director = NullIfWhiteSpace(Director);
        var actors = ParsedActors();
        var synopsis = NullIfWhiteSpace(Synopsis);
        var seriesTitle = IsTvEpisode
            ? NullIfWhiteSpace(SeriesTitle)
            : baseline.SeriesTitle;
        var episodeTitle = IsTvEpisode
            ? NullIfWhiteSpace(EpisodeTitle)
            : baseline.EpisodeTitle;
        int? seasonNumber = IsTvEpisode
            ? TryNonNegativeNumber(SeasonNumberText, out var parsedSeason)
                ? parsedSeason
                : null
            : baseline.SeasonNumber;
        int? episodeNumber = IsTvEpisode
            ? TryPositiveNumber(EpisodeNumberText, out var parsedEpisode)
                ? parsedEpisode
                : null
            : baseline.EpisodeNumber;

        if (!string.Equals(baseline.Title, title, StringComparison.Ordinal))
        {
            edited.Add(MetadataField.Title);
        }
        if (!string.Equals(
                baseline.OriginalTitle,
                originalTitle,
                StringComparison.Ordinal))
        {
            edited.Add(MetadataField.OriginalTitle);
        }
        if (baseline.ReleaseDate != releaseDate)
        {
            edited.Add(MetadataField.ReleaseDate);
        }
        if (!string.Equals(
                baseline.Director,
                director,
                StringComparison.Ordinal))
        {
            edited.Add(MetadataField.Director);
        }
        if (!baseline.Actors.SequenceEqual(actors))
        {
            edited.Add(MetadataField.Actors);
        }
        if (!string.Equals(
                baseline.Synopsis,
                synopsis,
                StringComparison.Ordinal))
        {
            edited.Add(MetadataField.Synopsis);
        }
        if (!string.Equals(baseline.SeriesTitle, seriesTitle, StringComparison.Ordinal))
        {
            edited.Add(MetadataField.SeriesTitle);
        }
        if (!string.Equals(baseline.EpisodeTitle, episodeTitle, StringComparison.Ordinal))
        {
            edited.Add(MetadataField.EpisodeTitle);
        }
        if (baseline.SeasonNumber != seasonNumber)
        {
            edited.Add(MetadataField.SeasonNumber);
        }
        if (baseline.EpisodeNumber != episodeNumber)
        {
            edited.Add(MetadataField.EpisodeNumber);
        }
        if (_selectedBaseline is null)
        {
            if (!string.Equals(
                    OriginalRecord.Poster,
                    poster,
                    StringComparison.OrdinalIgnoreCase))
            {
                edited.Add(MetadataField.Poster);
            }
        }
        else if (SelectedPosterSourcePath is not null || RemovePoster)
        {
            edited.Add(MetadataField.Poster);
        }

        return baseline with
        {
            Title = title,
            OriginalTitle = originalTitle,
            ReleaseDate = releaseDate,
            Director = director,
            Actors = actors,
            Synopsis = synopsis,
            Poster = poster,
            MediaType = _mediaType,
            SeriesTitle = seriesTitle,
            EpisodeTitle = episodeTitle,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            UserEditedFields = edited
        };
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
            return ApplyDetails(await _getDetails(candidate, linked.Token));
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

    private bool ApplyDetails(MetadataDetails details)
    {
        if (details.MediaType == MediaType.TvEpisode
            && (string.IsNullOrWhiteSpace(details.SeriesTitle)
                || details.SeasonNumber is null or < 0
                || details.EpisodeNumber is not > 0))
        {
            TvValidationMessage =
                "시리즈명과 시즌·에피소드 번호에 올바른 값을 입력하세요.";
            ErrorMessage = LookupError(MetadataProviderFailureKind.InvalidResponse);
            return false;
        }

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

        _mediaType = _selectedBaseline.MediaType;
        _seriesTitle = _selectedBaseline.SeriesTitle ?? string.Empty;
        _episodeTitle = _selectedBaseline.EpisodeTitle ?? string.Empty;
        _seasonNumberText = NumberText(_selectedBaseline.SeasonNumber);
        _episodeNumberText = NumberText(_selectedBaseline.EpisodeNumber);
        Raise(nameof(IsTvEpisode));
        Raise(nameof(SeriesTitle));
        Raise(nameof(EpisodeTitle));
        Raise(nameof(SeasonNumberText));
        Raise(nameof(EpisodeNumberText));
        Raise(nameof(CanApplyTvEpisode));
        TvValidationMessage = null;

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
        SetLookupStep(LookupStep.None);
        ErrorMessage = null;
        return true;
    }

    private void RegenerateEpisodeTitle()
    {
        if (!TryReadTvDraft(
                out var seriesTitle,
                out var seasonNumber,
                out var episodeNumber))
        {
            return;
        }

        Title = MetadataEnrichmentService.BuildEpisodeTitle(
            seriesTitle,
            NullIfWhiteSpace(EpisodeTitle),
            seasonNumber,
            episodeNumber);
        TvValidationMessage = null;
    }

    private bool TryReadTvDraft(
        out string seriesTitle,
        out int seasonNumber,
        out int episodeNumber)
    {
        seriesTitle = SeriesTitle.Trim();
        seasonNumber = 0;
        episodeNumber = 0;
        return IsTvEpisode
            && !string.IsNullOrWhiteSpace(seriesTitle)
            && TryNonNegativeNumber(SeasonNumberText, out seasonNumber)
            && TryPositiveNumber(EpisodeNumberText, out episodeNumber);
    }

    private static bool TryNonNegativeNumber(string value, out int number) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number)
        && number >= 0;

    private static bool TryPositiveNumber(string value, out int number) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number)
        && number > 0;

    private static string NumberText(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SetLookupStep(LookupStep step)
    {
        if (_lookupStep == step)
        {
            return;
        }

        _lookupStep = step;
        Raise(nameof(IsSearchResultStep));
        Raise(nameof(IsSeasonStep));
        Raise(nameof(IsEpisodeStep));
    }

    private static readonly Regex LookupHintPattern = new(
        @"^(?<title>.+?)\s+S(?<season>[0-9]+)\s*E(?<episode>[0-9]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string LookupError(MetadataProviderFailureKind kind) =>
        kind switch
        {
            MetadataProviderFailureKind.Authentication =>
                ".env의 DABOM_TMDB_ACCESS_TOKEN을 확인하세요.",
            MetadataProviderFailureKind.Transient =>
                "온라인 메타데이터 조회에 실패했습니다. 잠시 후 다시 시도하세요.",
            _ => "메타데이터 응답을 처리하지 못했습니다. 다시 시도하세요."
        };

    private enum LookupStep
    {
        None,
        SearchResult,
        Season,
        Episode
    }
}
