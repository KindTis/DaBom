using System.IO;
using System.Text;

namespace Dabom.Library;

internal static class LibraryRules
{
    internal static string DisplayTitle(string path, VideoRecord record) =>
        string.IsNullOrWhiteSpace(record.Title)
            ? Path.GetFileNameWithoutExtension(path)
            : record.Title.Trim();

    internal static string DurationText(long? ticks)
    {
        if (ticks is not long value) return "—";
        var duration = TimeSpan.FromTicks(value);
        return duration.TotalHours >= 1
            ? $"{(long)duration.TotalHours}시간 {duration.Minutes}분"
            : $"{duration.Minutes}분";
    }

    internal static bool Matches(string path, VideoRecord record, string query)
    {
        var needle = Normalize(query.Trim());
        if (needle.Length == 0)
        {
            return true;
        }

        return new[]
        {
            DisplayTitle(path, record),
            record.OriginalTitle,
            record.Director,
            string.Join(' ', record.Actors),
            Path.GetFileNameWithoutExtension(path)
        }.Any(value => Normalize(value ?? string.Empty)
            .Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? SelectFeaturedPath(
        IEnumerable<string> currentPaths,
        IReadOnlyDictionary<string, VideoRecord> records,
        Func<int, int> pickIndex)
    {
        var paths = currentPaths.ToArray();
        var unplayed = paths.Where(path => records[path].LastPlayedUtc is null).ToArray();
        if (unplayed.Length > 0)
        {
            return unplayed[pickIndex(unplayed.Length)];
        }

        return paths
            .OrderBy(path => records[path].LastPlayedUtc)
            .FirstOrDefault();
    }

    internal static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC);
}
