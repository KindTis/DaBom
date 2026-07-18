using Dabom.Library;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;

namespace Dabom.Main;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LibraryStore _store;
    private readonly ILibraryScanner _scanner;
    private readonly Func<string, bool> _launch;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<int, int> _pickIndex;
    private readonly ListCollectionView _visibleVideos;
    private LibraryData _data;

    public MainViewModel(LibraryStore store, ILibraryScanner scanner, LibraryData data)
        : this(store, scanner, data, LaunchWithWindows,
            () => DateTimeOffset.UtcNow, maximum => Random.Shared.Next(maximum)) { }

    internal MainViewModel(
        LibraryStore store,
        ILibraryScanner scanner,
        LibraryData data,
        Func<string, bool> launch,
        Func<DateTimeOffset> utcNow,
        Func<int, int> pickIndex)
    {
        _store = store;
        _scanner = scanner;
        _data = data;
        _launch = launch;
        _utcNow = utcNow;
        _pickIndex = pickIndex;
        Locations = new(data.Locations);
        _visibleVideos = (ListCollectionView)CollectionViewSource.GetDefaultView(Videos);
        _visibleVideos.Filter = item => ((VideoItemViewModel)item).Matches(SearchText);
        ApplySort();
        if (!store.CanSave)
        {
            StatusMessage = store.LoadWarning
                ?? $"라이브러리 저장이 비활성화되었습니다: {store.JsonPath}";
        }
    }

    public ObservableCollection<string> Locations { get; }
    public ObservableCollection<VideoItemViewModel> Videos { get; } = [];
    public ObservableCollection<ScanWarning> Warnings { get; } = [];
    public System.ComponentModel.ICollectionView VisibleVideos => _visibleVideos;
    public int VisibleCount => VisibleVideos.Cast<object>().Count();

    private VideoItemViewModel? _selectedVideo;
    public VideoItemViewModel? SelectedVideo
    {
        get => _selectedVideo;
        set => Set(ref _selectedVideo, value);
    }

    private VideoItemViewModel? _featuredVideo;
    public VideoItemViewModel? FeaturedVideo
    {
        get => _featuredVideo;
        private set => Set(ref _featuredVideo, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value)) Raise(nameof(CanMutateLibrary));
        }
    }

    private bool _isChangingLocations;
    public bool IsChangingLocations
    {
        get => _isChangingLocations;
        private set
        {
            if (Set(ref _isChangingLocations, value)) Raise(nameof(CanMutateLibrary));
        }
    }

    private bool _isRecordingPlayback;
    public bool IsRecordingPlayback
    {
        get => _isRecordingPlayback;
        private set
        {
            if (Set(ref _isRecordingPlayback, value)) Raise(nameof(CanMutateLibrary));
        }
    }

    public bool CanMutateLibrary =>
        _store.CanSave && !IsScanning && !IsChangingLocations && !IsRecordingPlayback;

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
            if (Set(ref _lastScanUtc, value)) Raise(nameof(LastScanText));
        }
    }

    public string LastScanText => LastScanUtc is DateTimeOffset utc
        ? $"마지막 확인: {utc.ToLocalTime():yyyy-MM-dd HH:mm}"
        : "마지막 확인: —";

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            VisibleVideos.Refresh();
            Raise(nameof(VisibleCount));
            if (SelectedVideo is not null && !SelectedVideo.Matches(value))
            {
                SelectedVideo = null;
            }
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

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
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
                var next = (old ?? new VideoRecord()) with
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
            ApplyCurrentVideos(result.Videos.Keys);
            ReplaceWarnings(result.Warnings);
            LastScanUtc = _utcNow();
            FeaturedVideo = PickFeatured();
            StatusMessage = result.Warnings.Count == 0
                ? "폴더 확인을 마쳤습니다."
                : $"폴더 확인을 마쳤습니다. 경고 {result.Warnings.Count}건";
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

    private void ApplyCurrentVideos(IEnumerable<string> currentPaths)
    {
        var current = currentPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = Videos.ToDictionary(video => video.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in current)
        {
            var record = _data.VideosByPath[path];
            if (existing.TryGetValue(path, out var item))
            {
                item.Update(record, _store);
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

        VisibleVideos.Refresh();
        Raise(nameof(VisibleCount));
    }

    private void ReplaceWarnings(IEnumerable<ScanWarning> warnings)
    {
        Warnings.Clear();
        foreach (var warning in warnings) Warnings.Add(warning);
    }

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
        _visibleVideos.Refresh();
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
