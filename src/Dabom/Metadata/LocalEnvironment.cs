using System.IO;

namespace Dabom.Metadata;

internal static class LocalEnvironment
{
    internal static string? ReadFromLocalApplicationData(
        string localApplicationDataPath,
        string variableName)
    {
        var envPath = Path.Combine(
            Path.GetFullPath(localApplicationDataPath),
            "Dabom",
            ".env");
        try
        {
            var prefix = $"{variableName}=";
            foreach (var rawLine in File.ReadLines(envPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line[prefix.Length..].Trim().Trim('"');
                }
            }

            return null;
        }
        catch (Exception error) when (
            error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
