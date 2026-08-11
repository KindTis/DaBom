using Dabom.Library;
using Dabom.Metadata;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;

namespace Dabom.Main;

public enum LibraryFilterKind
{
    All,
    MissingMetadata,
    Genre
}

public sealed record LibraryFilterOption(
    LibraryFilterKind Kind,
    string? Genre,
    int Count,
    bool StartsGenreSection = false)
{
    public string Label => Kind switch
    {
        LibraryFilterKind.All => "전체 영상",
        LibraryFilterKind.MissingMetadata => "메타데이터 없음",
        _ => Genre ?? string.Empty
    };

    public string AutomationName => $"{Label}, {Count}편";
    public override string ToString() => Label;
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly LibraryStore _store;
    private readonly ILibraryScanner _scanner;
    private readonly Func<string, bool> _launch;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<int, int> _pickIndex;
    private readonly MetadataEnrichmentService? _metadataEnrichment;
    private readonly ListCollectionView _visibleVideos;
    private LibraryData _data;
    private LibraryFilterOption _selectedFilter = new(
        LibraryFilterKind.All,
        null,
        0);

    public MainViewModel(
        LibraryStore store,
        ILibraryScanner scanner,
        MetadataEnrichmentService metadataEnrichment,
        LibraryData data)
        : this(store, scanner, data, LaunchWithWindows,
            () => DateTimeOffset.UtcNow,
            maximum => Random.Shared.Next(maximum),
            metadataEnrichment)
    {
    }

    internal MainViewModel(
        LibraryStore store,
        ILibraryScanner scanner,
        LibraryData data,
        Func<string, bool> launch,
        Func<DateTimeOffset> utcNow,
        Func<int, int> pickIndex,
        MetadataEnrichmentService? metadataEnrichment = null)
    {
        _store = store;
        _scanner = scanner;
        _data = data;
        _launch = launch;
        _utcNow = utcNow;
        _pickIndex = pickIndex;
        _metadataEnrichment = metadataEnrichment;
        RescanCommand = new AsyncRelayCommand(ScanAsync, () => CanMutateLibrary);
        PlayCommand = new AsyncRelayCommand(
            () => SelectedVideo is null ? Task.CompletedTask : PlayAsync(SelectedVideo),
            () => CanMutateLibrary && SelectedVideo is not null);
        PlayFeaturedCommand = new AsyncRelayCommand(
            () => HeroVideo is { } video ? PlayAsync(video) : Task.CompletedTask,
            () => CanMutateLibrary && HeroVideo is not null);
        OpenMetadataCommand = new RelayCommand(
            _ =>
            {
                var editor = CreateMetadataEditor();
                if (editor is not null) MetadataEditRequested?.Invoke(this, editor);
            },
            _ => CanMutateLibrary && SelectedVideo is not null);
        RemoveLocationCommand = new RelayCommand(
            path => _ = RemoveLocationAsync((string)path!),
            path => CanMutateLibrary && path is string);
        Locations = new(data.Locations);
        _isInitialLibraryLoad = Locations.Count > 0;
        _visibleVideos = (ListCollectionView)CollectionViewSource.GetDefaultView(Videos);
        _visibleVideos.Filter = item =>
            MatchesVisibleConditions((VideoItemViewModel)item);
        ApplySort();
        RefreshFilterOptions();
        if (!store.CanSave)
        {
            StatusMessage = store.LoadWarning
                ?? $"라이브러리 저장이 비활성화되었습니다: {store.JsonPath}";
        }
    }

    public ObservableCollection<string> Locations { get; }
    public ObservableCollection<VideoItemViewModel> Videos { get; } = [];
    public ObservableCollection<LibraryItemViewModel> VisibleItems { get; } = [];
    public ObservableCollection<LibraryFilterOption> FilterOptions { get; } = [];
    public ObservableCollection<ScanWarning> Warnings { get; } = [];
    public System.ComponentModel.ICollectionView VisibleVideos => _visibleVideos;
    public int VisibleCount => VisibleVideos.Cast<object>().Count();
    public int DisplayItemCount => VisibleItems.Count;
    public ICommand RescanCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PlayFeaturedCommand { get; }
    public ICommand OpenMetadataCommand { get; }
    public ICommand RemoveLocationCommand { get; }
    public event EventHandler<MetadataEditorViewModel>? MetadataEditRequested;

    private LibraryItemViewModel? _selectedItem;
    private SeasonGroupKey? _activeSeasonKey;
    private string _seasonDisplayTitle = string.Empty;
    private SeasonItemViewModel? _activeSeason;

    public LibraryItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!Set(ref _selectedItem, value)) return;
            Raise(nameof(SelectedVideo));
            RefreshCommandStates();
        }
    }

    public VideoItemViewModel? SelectedVideo
    {
        get => SelectedItem as VideoItemViewModel;
        set => SelectedItem = value;
    }

    public bool IsSeasonView => _activeSeasonKey is not null;
    public string SeasonHeading => _activeSeasonKey is { } key
        ? $"{_seasonDisplayTitle} · 시즌 {key.SeasonNumber}"
        : string.Empty;

    public SeasonItemViewModel? ActiveSeason
    {
        get => _activeSeason;
        private set
        {
            if (!Set(ref _activeSeason, value)) return;
            Raise(nameof(HeroVideo));
            RefreshCommandStates();
        }
    }

    public VideoItemViewModel? HeroVideo => ActiveSeason?.IntroEpisode ?? FeaturedVideo;
    public string ToolbarContextLabel => IsSeasonView ? "에피소드" : "내 영상";
    public int ToolbarItemCount => IsSeasonView ? DisplayItemCount : VisibleCount;
    public string ToolbarGuidance => IsSeasonView
        ? "현재 조건의 에피소드를 표시하고 있습니다."
        : "현재 조건의 영상을 표시하고 있습니다.";

    private VideoItemViewModel? _featuredVideo;
    public VideoItemViewModel? FeaturedVideo
    {
        get => _featuredVideo;
        private set
        {
            if (!Set(ref _featuredVideo, value)) return;
            Raise(nameof(HeroVideo));
            RefreshCommandStates();
        }
    }

    private bool _isInitialLibraryLoad;
    public bool IsLibraryLoading =>
        _isInitialLibraryLoad || IsScanning || IsChangingLocations;

    private bool _hasCompletedLibraryScan;
    public bool HasCompletedLibraryScan
    {
        get => _hasCompletedLibraryScan;
        private set => Set(ref _hasCompletedLibraryScan, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value))
            {
                Raise(nameof(CanMutateLibrary));
                Raise(nameof(IsLibraryLoading));
                RefreshCommandStates();
            }
        }
    }

    private bool _isChangingLocations;
    public bool IsChangingLocations
    {
        get => _isChangingLocations;
        private set
        {
            if (Set(ref _isChangingLocations, value))
            {
                Raise(nameof(CanMutateLibrary));
                Raise(nameof(IsLibraryLoading));
                RefreshCommandStates();
            }
        }
    }

    private bool _isRecordingPlayback;
    public bool IsRecordingPlayback
    {
        get => _isRecordingPlayback;
        private set
        {
            if (Set(ref _isRecordingPlayback, value))
            {
                Raise(nameof(CanMutateLibrary));
                RefreshCommandStates();
            }
        }
    }

    public bool CanMutateLibrary =>
        _store.CanSave && !IsScanning && !IsChangingLocations && !IsRecordingPlayback;

    public void NotifyMissingSelection()
    {
        if (CanMutateLibrary) StatusMessage = "먼저 영상을 선택하세요";
    }

    public bool OpenSeason(SeasonItemViewModel season)
    {
        if (!VisibleItems.Contains(season)) return false;
        _activeSeasonKey = season.Key;
        _seasonDisplayTitle = season.DisplayTitle;
        SelectedItem = null;
        RefreshLibraryView(false);
        RaiseSeasonContext();
        return true;
    }

    public void CloseSeason()
    {
        if (_activeSeasonKey is null) return;
        _activeSeasonKey = null;
        SelectedItem = null;
        RefreshLibraryView(false);
        RaiseSeasonContext();
    }

    internal SeasonItemViewModel? FindSeason(SeasonGroupKey key) =>
        VisibleItems.OfType<SeasonItemViewModel>()
            .SingleOrDefault(season => season.Key == key);

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        internal set => Set(ref _statusMessage, value);
    }

    private DateTimeOffset? _lastScanUtc;
    public DateTimeOffset? LastScanUtc
    {
        get => _lastScanUtc;
        private set
        {
            if (!Set(ref _lastScanUtc, value)) return;
            Raise(nameof(LastScanText));
        }
    }

    public string LastScanText => LastScanUtc is DateTimeOffset utc
        ? $"마지막 확인: {utc.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "마지막 확인: —";

    public LibraryFilterOption? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is null || SameFilter(_selectedFilter, value)) return;
            var removeDisappearedGenre = _selectedFilter.Kind == LibraryFilterKind.Genre
                && !HasCurrentGenre(_selectedFilter.Genre);
            _selectedFilter = value;
            Raise();
            Raise(nameof(IsFilterActive));
            Raise(nameof(FilterAutomationName));
            RefreshLibraryView(removeDisappearedGenre);
        }
    }

    public bool IsFilterActive => _selectedFilter.Kind != LibraryFilterKind.All;
    public string FilterAutomationName => $"영상 필터: {_selectedFilter.Label}";

    public bool IsFilterEmptyStateVisible => IsSeasonView
        ? DisplayItemCount == 0
        : IsFilterActive && Videos.Count > 0 && VisibleCount == 0;

    public bool IsMetadataCompleteFilterEmpty =>
        _selectedFilter.Kind == LibraryFilterKind.MissingMetadata
        && Videos.Count > 0
        && Videos.All(video => !NeedsMetadata(video.Record.MetadataStatus));

    public string FilterEmptyTitle => IsMetadataCompleteFilterEmpty
        ? "모든 영상의 메타데이터가 준비되었습니다."
        : "현재 검색과 필터에 맞는 영상이 없습니다.";

    public string FilterEmptyGuidance => IsMetadataCompleteFilterEmpty
        ? "다른 영상을 보려면 ‘전체 영상’을 선택하세요."
        : "검색어를 지우거나 ‘전체 영상’을 선택하세요.";

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            RefreshLibraryView(true);
        }
    }

    private VideoSort _selectedSort;
    public VideoSort SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (Set(ref _selectedSort, value)) ApplySort();
        }
    }

    public async Task InitializeAsync(string? startupWarning)
    {
        if (!string.IsNullOrWhiteSpace(startupWarning))
        {
            StatusMessage = startupWarning;
        }
        else if (Locations.Count == 0)
        {
            StatusMessage = "보관 위치를 추가해 동영상 라이브러리를 시작하세요.";
        }

        if (Locations.Count > 0)
        {
            ApplyCurrentVideos(_data.VideosByPath.Keys.Where(path =>
                _data.Locations.Any(location => IsWithinLocation(path, location))));
            await ScanAsync(preserveFeatured: true);
        }

        if (_isInitialLibraryLoad)
        {
            _isInitialLibraryLoad = false;
            Raise(nameof(IsLibraryLoading));
        }
    }

    private static bool IsWithinLocation(string path, string location)
    {
        var relative = Path.GetRelativePath(location, path);
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    public Task ScanAsync() => ScanAsync(false);

    private async Task ScanAsync(bool preserveFeatured)
    {
        if (IsScanning || IsRecordingPlayback) return;
        IsScanning = true;
        HasCompletedLibraryScan = false;
        try
        {
            var result = await _scanner.ScanAsync(
                _data.Locations, _data.VideosByPath, CancellationToken.None);
            var nextRecords = new Dictionary<string, VideoRecord>(
                _data.VideosByPath, StringComparer.OrdinalIgnoreCase);
            var cacheChanged = false;
            foreach (var scanned in result.Videos.Values)
            {
                nextRecords.TryGetValue(scanned.Path, out var old);
                var next = (old ?? new VideoRecord
                {
                    Title = Path.GetFileNameWithoutExtension(scanned.Path),
                    MetadataStatus = MetadataStatus.Pending
                }) with
                {
                    FileSizeBytes = scanned.FileSizeBytes,
                    LastWriteTimeUtc = scanned.LastWriteTimeUtc,
                    DurationTicks = scanned.DurationTicks
                };
                cacheChanged |= old is null
                    || old.FileSizeBytes != next.FileSizeBytes
                    || old.LastWriteTimeUtc != next.LastWriteTimeUtc
                    || old.DurationTicks != next.DurationTicks;
                nextRecords[scanned.Path] = next;
            }

            var nextData = _data with { VideosByPath = nextRecords };
            if (cacheChanged)
            {
                await _store.SaveAsync(nextData);
            }

            _data = nextData;
            ApplyCurrentVideos(result.Videos.Keys, preserveFeatured);
            ReplaceWarnings(result.Warnings);
            LastScanUtc = _utcNow();
            HasCompletedLibraryScan = true;
            var summary = _metadataEnrichment is null
                ? new MetadataRunSummary(0, 0, 0, false)
                : await _metadataEnrichment.EnrichAsync(
                    _data.VideosByPath,
                    result.Videos.Keys.ToArray(),
                    CommitEnrichedRecordAsync,
                    ShowMetadataProgress,
                    CancellationToken.None);
            StatusMessage = summary.AuthenticationFailed
                ? $"메타데이터 실패 {summary.Failed}건. .env의 DABOM_TMDB_ACCESS_TOKEN을 확인한 뒤 다시 탐색하세요."
                : summary.Matched + summary.NotFound + summary.Failed == 0
                    ? result.Warnings.Count == 0
                        ? "폴더 확인을 마쳤습니다."
                        : $"폴더 확인을 마쳤습니다. 경고 {result.Warnings.Count}건"
                    : $"메타데이터 적용 완료 · 성공 {summary.Matched} · 결과 없음 {summary.NotFound} · 실패 {summary.Failed}";
        }
        catch (Exception error)
        {
            StatusMessage = $"폴더 확인에 실패했습니다: {error.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public Task<bool> AddLocationAsync(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusMessage = "올바른 보관 위치를 선택하세요.";
            return Task.FromResult(false);
        }

        if (_data.Locations.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "이미 등록된 보관 위치입니다";
            return Task.FromResult(false);
        }

        return CommitLocationsAsync([.. _data.Locations, normalized]);
    }

    public Task<bool> RemoveLocationAsync(string path)
    {
        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(false);
        }

        if (!_data.Locations.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        return CommitLocationsAsync(_data.Locations
            .Where(location => !location.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray());
    }

    private async Task<bool> CommitLocationsAsync(string[] locations)
    {
        if (!_store.CanSave)
        {
            StatusMessage = _store.LoadWarning
                ?? $"라이브러리 저장이 비활성화되었습니다: {_store.JsonPath}";
            return false;
        }
        if (!CanMutateLibrary) return false;

        IsChangingLocations = true;
        try
        {
            var normalized = locations
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var next = _data with { Locations = normalized };
            await _store.SaveAsync(next);
            _data = next;
            Locations.Clear();
            foreach (var location in normalized) Locations.Add(location);
            await ScanAsync();
            return true;
        }
        catch (Exception error)
        {
            StatusMessage = $"보관 위치를 저장하지 못했습니다: {error.Message}";
            return false;
        }
        finally
        {
            IsChangingLocations = false;
        }
    }

    public async Task PlayAsync(VideoItemViewModel video)
    {
        if (!CanMutateLibrary) return;
        IsRecordingPlayback = true;
        try
        {
            try
            {
                if (!_launch(video.Path))
                {
                    throw new InvalidOperationException("기본 앱을 실행하지 못했습니다.");
                }
            }
            catch (Exception error)
            {
                StatusMessage = $"영상을 재생하지 못했습니다: {error.Message}";
                return;
            }

            var updated = video.Record with { LastPlayedUtc = _utcNow() };
            var records = new Dictionary<string, VideoRecord>(
                _data.VideosByPath, StringComparer.OrdinalIgnoreCase)
            {
                [video.Path] = updated
            };
            var next = _data with { VideosByPath = records };
            try
            {
                await _store.SaveAsync(next);
                _data = next;
                video.Update(updated);
                if (IsSeasonView) RefreshLibraryView(false);
                StatusMessage = "영상을 기본 앱으로 실행했습니다.";
            }
            catch (Exception error)
            {
                StatusMessage = $"영상은 실행했지만 재생 이력 저장 실패: {error.Message}";
            }
        }
        finally
        {
            IsRecordingPlayback = false;
        }
    }

    public MetadataEditorViewModel? CreateMetadataEditor() =>
        SelectedVideo is null || !CanMutateLibrary
            ? null
            : new MetadataEditorViewModel(
                SelectedVideo.Path,
                SelectedVideo.Record,
                SelectedVideo.Poster,
                CommitMetadataAsync,
                _metadataEnrichment is null
                    ? null
                    : _metadataEnrichment.SearchManualAsync,
                _metadataEnrichment is null
                    ? null
                    : _metadataEnrichment.GetManualDetailsAsync);

    private async Task<string?> CommitMetadataAsync(
        MetadataEditorViewModel editor,
        CancellationToken cancellationToken)
    {
        string? newPoster = editor.OriginalRecord.Poster;
        string? createdPoster = null;
        try
        {
            if (editor.SelectedPosterSourcePath is not null)
            {
                createdPoster = await _store.ImportPosterAsync(
                    editor.Path, editor.SelectedPosterSourcePath, cancellationToken);
                newPoster = createdPoster;
            }
            else if (editor.RemovePoster)
            {
                newPoster = null;
            }
            else if (editor.SelectedPosterUri is { } remotePoster)
            {
                createdPoster = await _metadataEnrichment!.DownloadPosterAsync(
                    remotePoster,
                    cancellationToken);
                newPoster = createdPoster;
            }
            else if (editor.HasSelectedResult)
            {
                newPoster = null;
            }

            var updated = editor.BuildRecord(newPoster);
            var records = new Dictionary<string, VideoRecord>(
                _data.VideosByPath, StringComparer.OrdinalIgnoreCase)
            {
                [editor.Path] = updated
            };
            var next = _data with { VideosByPath = records };
            await _store.SaveAsync(next, createdPoster, cancellationToken);

            _data = next;
            var video = Videos.Single(video =>
                video.Path.Equals(editor.Path, StringComparison.OrdinalIgnoreCase));
            video.Update(updated);
            await video.LoadPosterAsync();
            RefreshLibraryView(true);

            if (!string.Equals(
                editor.OriginalRecord.Poster, newPoster, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _store.DeletePoster(editor.OriginalRecord.Poster);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    StatusMessage =
                        $"메타데이터는 저장했지만 이전 포스터를 정리하지 못했습니다: {error.Message}";
                }
            }

            return null;
        }
        catch (Exception error)
        {
            return $"메타데이터를 저장하지 못했습니다: {error.Message}";
        }
    }

    private async Task CommitEnrichedRecordAsync(
        string path,
        VideoRecord updated,
        string? createdPoster,
        CancellationToken cancellationToken)
    {
        var old = _data.VideosByPath[path];
        var records = new Dictionary<string, VideoRecord>(
            _data.VideosByPath,
            StringComparer.OrdinalIgnoreCase)
        {
            [path] = updated
        };
        var next = _data with { VideosByPath = records };
        await _store.SaveAsync(next, createdPoster, cancellationToken);

        _data = next;
        var video = Videos.Single(item =>
            item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        video.Update(updated);
        await video.LoadPosterAsync();
        RefreshLibraryView(true);

        if (!string.Equals(
            old.Poster,
            updated.Poster,
            StringComparison.OrdinalIgnoreCase))
        {
            try { _store.DeletePoster(old.Poster); }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException) { }
        }
    }

    private void ShowMetadataProgress(MetadataProgress progress)
    {
        StatusMessage =
            $"메타데이터 처리 {progress.Completed}/{progress.Total} · "
            + $"성공 {progress.Matched} · 결과 없음 {progress.NotFound} · "
            + $"실패 {progress.Failed} · {Path.GetFileName(progress.Path)}";
    }

    private void ApplyCurrentVideos(
        IEnumerable<string> currentPaths,
        bool preserveFeatured = false)
    {
        var current = currentPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = Videos.ToDictionary(video => video.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in current)
        {
            var record = _data.VideosByPath[path];
            if (existing.TryGetValue(path, out var item))
            {
                item.Update(record);
            }
            else
            {
                Videos.Add(new(path, record, _store));
            }
        }

        foreach (var item in Videos.Where(video => !current.Contains(video.Path)).ToArray())
        {
            if (ReferenceEquals(SelectedVideo, item)) SelectedVideo = null;
            Videos.Remove(item);
        }

        RefreshLibraryView(true);
        if (!preserveFeatured
            || FeaturedVideo is null
            || !current.Contains(FeaturedVideo.Path))
            FeaturedVideo = PickFeatured();
        var posterItems = Videos.Where(video => video.NeedsPosterLoad).ToArray();
        if (posterItems.Length > 0) _ = LoadPostersAsync(posterItems);
    }

    internal async Task LoadPostersAsync(VideoItemViewModel[] videos)
    {
        foreach (var video in videos)
        {
            if (!Videos.Contains(video)) continue;
            await video.LoadPosterAsync();
        }

        if (videos.Any(Videos.Contains)) RefreshLibraryView(false);
    }

    private void ReplaceWarnings(IEnumerable<ScanWarning> warnings)
    {
        Warnings.Clear();
        foreach (var warning in warnings) Warnings.Add(warning);
    }

    private void RefreshLibraryView(bool refreshFilterOptions)
    {
        if (refreshFilterOptions) RefreshFilterOptions();
        _visibleVideos.Refresh();
        RebuildVisibleItems();
        Raise(nameof(VisibleCount));
        Raise(nameof(IsFilterEmptyStateVisible));
        Raise(nameof(IsMetadataCompleteFilterEmpty));
        Raise(nameof(FilterEmptyTitle));
        Raise(nameof(FilterEmptyGuidance));
    }

    private Dictionary<SeasonGroupKey, VideoItemViewModel[]> CurrentSeasonGroups() =>
        Videos
            .Select(video => (Video: video, Key: SeasonGroupKey.From(video.Record)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, item => item.Video)
            .Where(group => group.Count() >= 2)
            .ToDictionary(group => group.Key, group => group.ToArray());

    private void RebuildVisibleItems()
    {
        var selected = SelectedItem;
        var selectedSeasonKey = (selected as SeasonItemViewModel)?.Key;
        var matching = _visibleVideos.Cast<VideoItemViewModel>().ToArray();
        var groups = CurrentSeasonGroups();
        var matchingGroups = matching
            .Select(video => (Video: video, Key: SeasonGroupKey.From(video.Record)))
            .Where(item => item.Key is not null && groups.ContainsKey(item.Key!))
            .GroupBy(item => item.Key!, item => item.Video)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var wasSeasonView = IsSeasonView;
        if (_activeSeasonKey is not null && !groups.ContainsKey(_activeSeasonKey))
        {
            _activeSeasonKey = null;
        }
        var emitted = new HashSet<SeasonGroupKey>();
        var items = new List<LibraryItemViewModel>();

        if (_activeSeasonKey is { } activeKey)
        {
            var wholeGroup = groups[activeKey];
            ActiveSeason = new SeasonItemViewModel(activeKey, wholeGroup, wholeGroup);
            var activeEpisodes = matching
                .Where(video => SeasonGroupKey.From(video.Record) == activeKey)
                .ToArray();
            if (activeEpisodes.Length > 0)
            {
                _seasonDisplayTitle = activeEpisodes[0].Record.SeriesTitle!.Trim();
            }
            items.AddRange(activeEpisodes);
        }
        else
        {
            ActiveSeason = null;
            foreach (var video in matching)
            {
                var key = SeasonGroupKey.From(video.Record);
                if (key is not null && groups.TryGetValue(key, out var wholeGroup))
                {
                    if (emitted.Add(key))
                    {
                        items.Add(new SeasonItemViewModel(
                            key,
                            matchingGroups[key],
                            wholeGroup));
                    }
                }
                else
                {
                    items.Add(video);
                }
            }
        }

        VisibleItems.Clear();
        foreach (var item in items) VisibleItems.Add(item);
        Raise(nameof(DisplayItemCount));
        Raise(nameof(ToolbarItemCount));
        Raise(nameof(SeasonHeading));
        if (wasSeasonView != IsSeasonView)
        {
            RaiseSeasonContext();
        }

        SelectedItem = wasSeasonView && !IsSeasonView
            ? null
            : selectedSeasonKey is null
            ? selected is VideoItemViewModel selectedVideo && items.Contains(selectedVideo)
                ? selectedVideo
                : null
                : items.OfType<SeasonItemViewModel>()
                    .SingleOrDefault(season => season.Key == selectedSeasonKey);
    }

    private void RaiseSeasonContext()
    {
        Raise(nameof(IsSeasonView));
        Raise(nameof(SeasonHeading));
        Raise(nameof(ToolbarContextLabel));
        Raise(nameof(ToolbarItemCount));
        Raise(nameof(ToolbarGuidance));
    }

    private void RefreshFilterOptions()
    {
        var searched = Videos.Where(video => video.Matches(SearchText)).ToArray();
        var genres = CurrentGenres().ToList();
        if (_selectedFilter.Kind == LibraryFilterKind.Genre
            && _selectedFilter.Genre is { } selectedGenre
            && !genres.Contains(selectedGenre, StringComparer.CurrentCultureIgnoreCase))
        {
            genres.Add(selectedGenre);
            genres.Sort(StringComparer.CurrentCultureIgnoreCase);
        }

        var options = new List<LibraryFilterOption>
        {
            new(LibraryFilterKind.All, null, searched.Length),
            new(
                LibraryFilterKind.MissingMetadata,
                null,
                searched.Count(video => NeedsMetadata(video.Record.MetadataStatus)))
        };
        options.AddRange(genres.Select((genre, index) => new LibraryFilterOption(
            LibraryFilterKind.Genre,
            genre,
            searched.Count(video => HasGenre(video.Record, genre)),
            index == 0)));

        FilterOptions.Clear();
        foreach (var option in options) FilterOptions.Add(option);
        _selectedFilter = FilterOptions.Single(option =>
            SameFilter(option, _selectedFilter));
        Raise(nameof(SelectedFilter));
        Raise(nameof(FilterAutomationName));
    }

    private IEnumerable<string> CurrentGenres() => Videos
        .SelectMany(video => video.Record.Genres)
        .Select(NormalizeGenre)
        .Where(genre => genre is not null)
        .Cast<string>()
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(genre => genre, StringComparer.CurrentCultureIgnoreCase);

    private bool MatchesVisibleConditions(VideoItemViewModel video) =>
        video.Matches(SearchText) && MatchesSelectedFilter(video.Record);

    private bool MatchesSelectedFilter(VideoRecord record) => _selectedFilter.Kind switch
    {
        LibraryFilterKind.MissingMetadata => NeedsMetadata(record.MetadataStatus),
        LibraryFilterKind.Genre => HasGenre(record, _selectedFilter.Genre),
        _ => true
    };

    private bool HasCurrentGenre(string? genre) =>
        genre is not null && CurrentGenres().Contains(
            genre,
            StringComparer.CurrentCultureIgnoreCase);

    private static bool HasGenre(VideoRecord record, string? genre) =>
        genre is not null && record.Genres
            .Select(NormalizeGenre)
            .Any(value => string.Equals(
                value,
                genre,
                StringComparison.CurrentCultureIgnoreCase));

    private static string? NormalizeGenre(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static bool NeedsMetadata(MetadataStatus status) => status is
        MetadataStatus.Unspecified
        or MetadataStatus.Pending
        or MetadataStatus.NotFound
        or MetadataStatus.Failed;

    private static bool SameFilter(
        LibraryFilterOption left,
        LibraryFilterOption right) =>
        left.Kind == right.Kind
        && string.Equals(
            left.Genre,
            right.Genre,
            StringComparison.CurrentCultureIgnoreCase);

    private VideoItemViewModel? PickFeatured()
    {
        var path = LibraryRules.SelectFeaturedPath(
            Videos.Select(video => video.Path), _data.VideosByPath, _pickIndex);
        return path is null
            ? null
            : Videos.Single(video => video.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplySort()
    {
        _visibleVideos.CustomSort = Comparer<VideoItemViewModel>.Create((left, right) => SelectedSort switch
        {
            VideoSort.ReleaseDate => CompareNullableDescending(
                left.Record.ReleaseDate, right.Record.ReleaseDate),
            VideoSort.FileModified => right.Record.LastWriteTimeUtc.CompareTo(left.Record.LastWriteTimeUtc),
            _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayTitle, right.DisplayTitle)
        });
        RefreshLibraryView(false);
    }

    private void RefreshCommandStates()
    {
        ((AsyncRelayCommand)RescanCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)PlayCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)PlayFeaturedCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenMetadataCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveLocationCommand).RaiseCanExecuteChanged();
    }

    private static int CompareNullableDescending<T>(T? left, T? right)
        where T : struct, IComparable<T> =>
        left.HasValue && right.HasValue ? right.Value.CompareTo(left.Value)
        : left.HasValue ? -1
        : right.HasValue ? 1
        : 0;

    private static bool LaunchWithWindows(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return true;
    }
}
