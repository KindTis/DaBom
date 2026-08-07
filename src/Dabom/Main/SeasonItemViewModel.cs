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
    internal SeasonItemViewModel(
        SeasonGroupKey key,
        IReadOnlyList<VideoItemViewModel> episodes,
        IReadOnlyList<VideoItemViewModel> wholeGroup)
    {
        Key = key;
        Episodes = episodes;
        DisplayTitle = episodes[0].Record.SeriesTitle!.Trim();
        Poster = wholeGroup.FirstOrDefault(video => video.HasPoster)?.Poster;
    }

    internal SeasonGroupKey Key { get; }
    internal IReadOnlyList<VideoItemViewModel> Episodes { get; }
    public string DisplayTitle { get; }
    public int SeasonNumber => Key.SeasonNumber;
    public int EpisodeCount => Episodes.Count;
    public string Summary => $"시즌 {SeasonNumber} · {EpisodeCount}편";
    public BitmapSource? Poster { get; }
    public bool HasPoster => Poster is not null;
    public override string AutomationName =>
        $"{DisplayTitle}, 시즌 {SeasonNumber}, {EpisodeCount}편, 시즌 열기";
}
