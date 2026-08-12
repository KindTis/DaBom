using Dabom.Library;
using System.Windows;
using System.Windows.Automation;

namespace Dabom;

public partial class VideoDeletionConfirmationWindow : Window
{
    internal VideoDeletionConfirmationWindow(
        string fileName,
        VideoFileStatus status)
    {
        InitializeComponent();
        FileNameText.Text = fileName;

        var fileExists = status == VideoFileStatus.Present;
        DescriptionText.Text = fileExists
            ? "파일을 휴지통으로 이동하고 목록에서도 제거합니다."
            : "파일을 찾을 수 없어 목록에서만 제거합니다.";
        var actionLabel = fileExists ? "휴지통으로 이동" : "목록에서 제거";
        ConfirmButton.Content = actionLabel;
        AutomationProperties.SetName(ConfirmButton, actionLabel);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
