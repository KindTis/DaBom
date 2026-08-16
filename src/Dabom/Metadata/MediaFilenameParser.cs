using System.IO;
using System.Text.RegularExpressions;
using Dabom.Library;

namespace Dabom.Metadata;

public sealed partial class MediaFilenameParser
{
    private static readonly HashSet<string> ReleaseTags = new(
    [
        "2160p", "1080p", "720p", "480p",
        "uhd", "bluray", "bdrip", "web-dl", "webdl", "webrip", "hdtv",
        "x264", "x265", "h264", "h265", "hevc", "av1", "10bit",
        "aac", "ddp", "dts", "dts-hd", "truehd", "atmos",
        "hdr", "hdr10", "dv"
    ], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?i)(?:^|[\s._-])S(?<season>\d{1,2})E(?<episode>\d{1,3})(?=$|[\s._-])")]
    private static partial Regex SeasonEpisodePattern();

    [GeneratedRegex(@"(?i)(?:^|[\s._-])E(?<episode>\d{1,3})(?=$|[\s._-])")]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"\b(?<whole>\d{1,2})\.(?<fraction>\d{1,2})\b")]
    private static partial Regex DecimalPattern();

    [GeneratedRegex(@"[._]")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpacePattern();

    [GeneratedRegex(@"(?<!\d)(?<year>(?:19|20)\d{2})(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"-[\p{L}\p{N}]+$")]
    private static partial Regex ReleaseGroupPattern();

    [GeneratedRegex(@"^\[[^]]+\]\s+(?=\S)")]
    private static partial Regex LeadingTagPattern();

    public MetadataQuery? Parse(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var episode = SeasonEpisodePattern().Match(stem);
        var season = episode.Success
            ? int.Parse(episode.Groups["season"].Value)
            : 1;
        if (!episode.Success) episode = EpisodePattern().Match(stem);
        if (episode.Success)
        {
            var seriesTitle = Clean(LeadingTagPattern().Replace(
                stem[..episode.Index], string.Empty));
            return HasSearchCharacter(seriesTitle)
                ? new(
                    MediaType.TvEpisode,
                    seriesTitle,
                    null,
                    season,
                    int.Parse(episode.Groups["episode"].Value))
                : null;
        }

        stem = RemoveReleaseGroup(stem);
        var cleaned = Clean(stem);
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var releaseIndex = Array.FindIndex(tokens, IsReleaseTag);
        if (releaseIndex < 0) releaseIndex = tokens.Length;
        var searchable = string.Join(' ', tokens[..releaseIndex]);
        var years = YearPattern().Matches(searchable);
        var year = years.Count == 0
            ? (int?)null
            : int.Parse(years[^1].Groups["year"].Value);
        if (years.Count > 0)
        {
            searchable = searchable[..years[^1].Index].Trim();
        }

        return HasSearchCharacter(searchable)
            ? new(MediaType.Movie, searchable, year)
            : null;
    }

    private static string Clean(string value)
    {
        const char decimalMarker = '\u001f';
        var decimalsProtected = DecimalPattern().Replace(
            value,
            match => match.Groups["whole"].Value
                + decimalMarker
                + match.Groups["fraction"].Value);
        return WhiteSpacePattern()
            .Replace(SeparatorPattern().Replace(decimalsProtected, " "), " ")
            .Replace(decimalMarker, '.')
            .Trim();
    }

    private static bool IsReleaseTag(string token)
    {
        var normalized = token.Trim('[', ']', '(', ')');
        return ReleaseTags.Contains(normalized)
            || Regex.IsMatch(
                normalized,
                @"^(?:x|h)?26[45]$",
                RegexOptions.IgnoreCase)
            || Regex.IsMatch(
                normalized,
                @"^(?:ddp|dts|aac)\d",
                RegexOptions.IgnoreCase);
    }

    private static string RemoveReleaseGroup(string stem)
    {
        var group = ReleaseGroupPattern().Match(stem);
        if (!group.Success) return stem;

        var fullTokens = Clean(stem).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        if (fullTokens.Length > 0 && IsReleaseTag(fullTokens[^1])) return stem;

        var prefix = stem[..group.Index];
        var prefixTokens = Clean(prefix).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        return YearPattern().IsMatch(prefix) || prefixTokens.Any(IsReleaseTag)
            ? prefix
            : stem;
    }

    private static bool HasSearchCharacter(string value) =>
        value.Any(char.IsLetterOrDigit);
}
