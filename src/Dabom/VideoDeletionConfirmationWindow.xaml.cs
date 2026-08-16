using Dabom.Library;
using Dabom.Main;
using System.IO;
using System.Windows;

namespace Dabom;

internal sealed record VideoDeletionConfirmationItem(
    string FileName,
    string? FolderHint,
    string ActionText,
    string AutomationName);

public partial class VideoDeletionConfirmationWindow : Window
{
    internal VideoDeletionConfirmationWindow(
        IReadOnlyList<VideoDeletionRequest> requests,
        int excludedCount)
    {
        InitializeComponent();
        DeletionItems.ItemsSource = BuildItems(requests);
        if (excludedCount > 0)
        {
            ExclusionText.Text =
                $"{excludedCount}개 항목은 파일 상태를 확인하지 못해 제외됩니다.";
            ExclusionText.Visibility = Visibility.Visible;
        }
    }

    internal static IReadOnlyList<VideoDeletionConfirmationItem> BuildItems(
        IReadOnlyList<VideoDeletionRequest> requests)
    {
        var pathsByFileName = requests
            .GroupBy(request => request.Video.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(request => request.Video.Path).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return requests.Select(request =>
        {
            var fileName = request.Video.FileName;
            var duplicatePaths = pathsByFileName[fileName];
            var folderHint = duplicatePaths.Length > 1
                ? DistinguishingFolder(request.Video.Path, duplicatePaths)
                : null;
            var actionText = request.Status == VideoFileStatus.Present
                ? "휴지통으로 이동하고 목록에서 제거"
                : "목록에서만 제거";
            var automationName = folderHint is null
                ? $"{fileName}, {actionText}"
                : $"{fileName}, {folderHint}, {actionText}";
            return new VideoDeletionConfirmationItem(
                fileName,
                folderHint,
                actionText,
                automationName);
        }).ToArray();
    }

    private static string DistinguishingFolder(
        string path,
        IReadOnlyList<string> duplicatePaths)
    {
        var target = DirectoryParts(path);
        var candidates = duplicatePaths.Select(DirectoryParts).ToArray();
        for (var count = 1; count <= target.Length; count++)
        {
            var suffix = target[^count..];
            if (candidates.Count(parts =>
                    parts.Length >= count
                    && parts[^count..].SequenceEqual(
                        suffix,
                        StringComparer.OrdinalIgnoreCase)) == 1)
            {
                return string.Join(Path.DirectorySeparatorChar, suffix);
            }
        }

        return string.Join(Path.DirectorySeparatorChar, target);
    }

    private static string[] DirectoryParts(string path) =>
        (Path.GetDirectoryName(path) ?? string.Empty).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private void OnConfirm(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
