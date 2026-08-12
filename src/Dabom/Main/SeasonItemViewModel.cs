using Dabom.Library;
using System.Text;
using System.Windows.Media.Imaging;

namespace Dabom.Main;

public abstract class LibraryItemViewModel : ViewModelBase
{
    public abstract string AutomationName { get; }
}

internal sealed record SeasonGroupKey(
    string? ProviderKey,
    string? SeriesResourceId,
    string? NormalizedSeriesTitle,
    int SeasonNumber)
{
    internal static SeasonGroupKey? From(VideoRecord record)
    {
        if (record.MediaType != MediaType.TvEpisode
            || record.SeasonNumber is not > 0
            || string.IsNullOrWhiteSpace(record.SeriesTitle))
        {
            return null;
        }

        var seriesReference = record.ProviderReferences.FirstOrDefault(reference =>
            string.Equals(
                reference.ResourceType,
                "tv-series",
                StringComparison.OrdinalIgnoreCase));
        return seriesReference is null
            ? new(
                null,
                null,
                record.SeriesTitle
                    .Normalize(NormalizationForm.FormKC)
                    .Trim()
                    .ToUpperInvariant(),
                record.SeasonNumber.Value)
            : new(
                seriesReference.ProviderKey,
                seriesReference.ResourceId,
                null,
                record.SeasonNumber.Value);
    }
}

public sealed class SeasonItemViewModel : LibraryItemViewModel
{
    private readonly IReadOnlyList<VideoItemViewModel> _wholeGroup;

    internal SeasonItemViewModel(
        SeasonGroupKey key,
        IReadOnlyList<VideoItemViewModel> episodes,
        IReadOnlyList<VideoItemViewModel> wholeGroup)
    {
        Key = key;
        Episodes = episodes;
        _wholeGroup = wholeGroup;
        DisplayTitle = episodes[0].Record.SeriesTitle!.Trim();
        Poster = wholeGroup.FirstOrDefault(video => video.HasPoster)?.Poster;
    }

    internal SeasonGroupKey Key { get; }
    internal IReadOnlyList<VideoItemViewModel> Episodes { get; }
    public string DisplayTitle { get; }
    public int SeasonNumber => Key.SeasonNumber;
    public int EpisodeCount => Episodes.Count;
    public string Summary => $"시즌 {SeasonNumber} · {EpisodeCount}편";
    public int TotalEpisodeCount => _wholeGroup.Count;
    public string TotalSummary => $"시즌 {SeasonNumber} · 총 {TotalEpisodeCount}편";
    public VideoItemViewModel IntroEpisode => SelectIntroEpisode(_wholeGroup);
    public string IntroLabel => _wholeGroup.Any(video => video.Record.LastPlayedUtc is null)
        ? "다음 미시청 에피소드"
        : "처음부터 보기";
    public string IntroHeading
    {
        get
        {
            var episode = IntroEpisode;
            var title = string.IsNullOrWhiteSpace(episode.Record.EpisodeTitle)
                ? episode.DisplayTitle
                : episode.Record.EpisodeTitle;
            return episode.Record.EpisodeNumber is > 0
                ? $"{episode.Record.EpisodeNumber}화 · {title}"
                : title;
        }
    }
    public BitmapSource? Poster { get; }
    public bool HasPoster => Poster is not null;
    public bool ContainsMissingFiles => _wholeGroup.Any(video => video.IsFileMissing);
    public double PosterOpacity => ContainsMissingFiles ? 0.5 : 1.0;
    public override string AutomationName => ContainsMissingFiles
        ? $"{DisplayTitle}, 시즌 {SeasonNumber}, {EpisodeCount}편, 파일 없음 포함, 시즌 열기"
        : $"{DisplayTitle}, 시즌 {SeasonNumber}, {EpisodeCount}편, 시즌 열기";

    private static VideoItemViewModel SelectIntroEpisode(
        IReadOnlyList<VideoItemViewModel> wholeGroup)
    {
        var unplayed = wholeGroup
            .Where(video => video.Record.LastPlayedUtc is null)
            .ToArray();
        var candidates = unplayed.Length > 0 ? unplayed : wholeGroup;
        return candidates
            .Select((episode, index) => (Episode: episode, Index: index))
            .OrderBy(item => item.Episode.Record.EpisodeNumber is > 0 ? 0 : 1)
            .ThenBy(item => item.Episode.Record.EpisodeNumber is > 0
                ? item.Episode.Record.EpisodeNumber.Value
                : int.MaxValue)
            .ThenBy(item => item.Index)
            .First()
            .Episode;
    }
}
