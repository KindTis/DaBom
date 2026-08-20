using System.Globalization;
using System.Text.Json;

namespace Dabom.Library;

internal static class MediaInfoTagReader
{
    internal static string[]? TryRead(string path)
    {
        try
        {
            using var mediaInfo = new global::MediaInfo.MediaInfo();
            mediaInfo.Option("Output", "JSON");
            if (mediaInfo.Open(path) == IntPtr.Zero) return null;

            var json = mediaInfo.Inform();
            return string.IsNullOrWhiteSpace(json) ? null : ParseTags(json);
        }
        catch
        {
            return null;
        }
    }

    internal static string[] ParseTags(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("media", out var media)
            || !media.TryGetProperty("track", out var tracks))
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var track in tracks.EnumerateArray())
        {
            switch (Value(track, "@type"))
            {
                case "Video":
                    AddVideoTags(tags, track);
                    break;
                case "Audio":
                    AddAudioTags(tags, track);
                    break;
            }
        }
        return tags.ToArray();
    }

    private static void AddVideoTags(List<string> tags, JsonElement track)
    {
        if (int.TryParse(Value(track, "Width"), out var width)
            && int.TryParse(Value(track, "Height"), out var height))
        {
            Add(tags, Math.Max(width, height) switch
            {
                >= 7680 => "8K",
                >= 3840 => "4K",
                >= 1920 => "FHD",
                >= 1280 => "HD",
                _ => null
            });
        }

        var hdr = $"{Value(track, "HDR_Format")} {Value(track, "HDR_Format_Compatibility")}";
        if (hdr.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
            Add(tags, "Dolby Vision");
        if (hdr.Contains("HDR10+", StringComparison.OrdinalIgnoreCase))
            Add(tags, "HDR10+");
        else if (hdr.Contains("HDR10", StringComparison.OrdinalIgnoreCase)
            || hdr.Contains("SMPTE ST 2086", StringComparison.OrdinalIgnoreCase))
            Add(tags, "HDR10");
        if (hdr.Contains("HLG", StringComparison.OrdinalIgnoreCase)) Add(tags, "HLG");
        if (!string.IsNullOrWhiteSpace(hdr)
            && !tags.Any(tag => tag is "Dolby Vision" or "HDR10" or "HDR10+" or "HLG"))
            Add(tags, "HDR");
    }

    private static void AddAudioTags(List<string> tags, JsonElement track)
    {
        var trackTags = new List<string>();
        var format = PreferredFormat(track);
        const string atmosSuffix = " with Dolby Atmos";
        if (format.EndsWith(atmosSuffix, StringComparison.OrdinalIgnoreCase))
        {
            Add(trackTags, format[..^atmosSuffix.Length]);
            Add(trackTags, "Dolby Atmos");
        }
        else
        {
            Add(trackTags, format);
            var features = Value(track, "Format_AdditionalFeatures");
            if (features.Contains("JOC", StringComparison.OrdinalIgnoreCase)
                || features.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
                Add(trackTags, "Dolby Atmos");
        }

        if (int.TryParse(
            Value(track, "Channels"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var channels)
            && channels > 0)
        {
            var layout = Value(track, "ChannelLayout").Split(
                ' ', StringSplitOptions.RemoveEmptyEntries);
            var lfe = layout.Length == 0
                ? channels is 3 or 6 or 7 or 8 ? 1 : 0
                : layout.Count(channel => channel.Equals("LFE", StringComparison.OrdinalIgnoreCase));
            var height = layout.Count(channel => channel.StartsWith('T'));
            Add(trackTags, height > 0
                ? $"{channels - lfe - height}.{lfe}.{height}"
                : $"{channels - lfe}.{lfe}");
        }

        if (trackTags.Count > 1) Add(tags, string.Join(" · ", trackTags));
    }

    private static string PreferredFormat(JsonElement track)
    {
        var commercial = Value(track, "Format_Commercial_IfAny");
        return string.IsNullOrWhiteSpace(commercial) ? Value(track, "Format") : commercial;
    }

    private static string Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

    private static void Add(List<string> tags, string? tag)
    {
        if (!string.IsNullOrWhiteSpace(tag)
            && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(tag);
        }
    }
}
