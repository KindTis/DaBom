using Dabom.Library;
using System.Windows.Media.Imaging;

namespace Dabom.Main;

public sealed class VideoItemViewModel : LibraryItemViewModel
{
    private readonly LibraryStore _store;
    private VideoRecord _record;
    private BitmapSource? _poster;
    private string? _posterLoadReference;
    private Task? _posterLoad;

    internal VideoItemViewModel(string path, VideoRecord record, LibraryStore store)
    {
        Path = path;
        _store = store;
        _record = record;
    }

    public string Path { get; }
    public override string AutomationName => $"{DisplayTitle}, 영상";
    public string FileName => System.IO.Path.GetFileName(Path);
    public VideoRecord Record => _record;
    public string DisplayTitle => LibraryRules.DisplayTitle(Path, _record);
    public string OriginalTitle => _record.OriginalTitle ?? string.Empty;
    public string ReleaseYear => _record.ReleaseDate?.Year.ToString() ?? "—";
    public string ReleaseDateText => _record.ReleaseDate is { } date
        ? $"{date.Year}년 {date.Month}월 {date.Day}일"
        : "—";
    public string Director => _record.Director ?? "—";
    public string ActorsText => _record.Actors.Length == 0 ? "—" : string.Join(", ", _record.Actors);
    public string GenresText => _record.Genres.Length == 0 ? "—" : string.Join(", ", _record.Genres);
    public string DurationText => LibraryRules.DurationText(_record.DurationTicks);
    public string FileSizeText => $"{_record.FileSizeBytes / 1024d / 1024d:N1} MB";
    public BitmapSource? Poster => _poster;
    public bool HasPoster => _poster is not null;
    internal bool NeedsPosterLoad =>
        _poster is null && !string.IsNullOrWhiteSpace(_record.Poster);
    internal bool Matches(string query) => LibraryRules.Matches(Path, _record, query);

    internal Task LoadPosterAsync()
    {
        var posterReference = _record.Poster;
        if (string.IsNullOrWhiteSpace(posterReference))
        {
            return Task.CompletedTask;
        }

        if (_posterLoad is not null
            && string.Equals(
                _posterLoadReference,
                posterReference,
                StringComparison.OrdinalIgnoreCase)
            && (!_posterLoad.IsCompleted || _poster is not null))
        {
            return _posterLoad;
        }

        _posterLoadReference = posterReference;
        _posterLoad = LoadPosterCoreAsync(posterReference);
        return _posterLoad;
    }

    internal void Update(VideoRecord record)
    {
        if (!string.Equals(
            _record.Poster,
            record.Poster,
            StringComparison.OrdinalIgnoreCase))
        {
            _poster = null;
            _posterLoadReference = null;
            _posterLoad = null;
        }
        _record = record;
        Raise(string.Empty);
    }

    private async Task LoadPosterCoreAsync(string posterReference)
    {
        var path = _store.ResolvePosterPath(posterReference);
        var poster = await Task.Run(() => PosterImage.TryLoad(path));
        if (!string.Equals(
            _record.Poster,
            posterReference,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _poster = poster;
        Raise(nameof(Poster));
        Raise(nameof(HasPoster));
    }
}
