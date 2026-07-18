using Dabom.Metadata;
using System.ComponentModel;
using System.Windows;

namespace Dabom;

public partial class MetadataWindow : Window
{
    public MetadataWindow()
    {
        InitializeComponent();
    }

    private void OnChoosePoster(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "포스터 이미지 선택",
            Filter = "이미지|*.jpg;*.jpeg;*.png;*.bmp"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ((MetadataEditorViewModel)DataContext).ChoosePoster(dialog.FileName);
        }
    }

    private void OnRemovePoster(object sender, RoutedEventArgs e) =>
        ((MetadataEditorViewModel)DataContext).MarkPosterRemoved();

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var saved = await ((MetadataEditorViewModel)DataContext).SaveAsync();
        if (saved)
        {
            DialogResult = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (((MetadataEditorViewModel)DataContext).IsSaving)
        {
            e.Cancel = true;
        }
    }
}
