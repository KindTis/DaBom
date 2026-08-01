using Dabom.Library;
using System.Windows.Media.Imaging;

namespace Dabom.Main;

public sealed class VideoItemViewModel : ViewModelBase
{
    private VideoRecord _record;
    private BitmapSource? _poster;

    internal VideoItemViewModel(string path, VideoRecord record, LibraryStore store)
    {
        Path = path;
        _record = record;
        _poster = PosterImage.TryLoad(store.ResolvePosterPath(record.Poster));
    }

    public string Path { get; }
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
    internal bool Matches(string query) => LibraryRules.Matches(Path, _record, query);

    internal void Update(VideoRecord record, LibraryStore store)
    {
        _record = record;
        _poster = PosterImage.TryLoad(store.ResolvePosterPath(record.Poster));
        Raise(string.Empty);
    }
}
