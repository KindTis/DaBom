using Dabom.Metadata;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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

    private async void OnSearchClick(object sender, RoutedEventArgs e) =>
        await RunSearchAsync();

    private void OnClearSearch(object sender, RoutedEventArgs e) => ClearSearch();

    private void ClearSearch()
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        viewModel.SearchText = string.Empty;
        viewModel.IsSearchPopupOpen = false;
        SearchBox.Focus();
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(viewModel.SearchText))
        {
            e.Handled = true;
            ClearSearch();
            return;
        }
        if (e.Key == Key.Escape && viewModel.IsSearchPopupOpen)
        {
            e.Handled = true;
            viewModel.IsSearchPopupOpen = false;
            SearchBox.Focus();
            return;
        }
        if (e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        if (!await viewModel.SearchAsync())
        {
            return;
        }
        if (viewModel.SearchCandidates.Count == 0)
        {
            SearchBox.Focus();
            return;
        }

        await Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                SearchResultsList.SelectedIndex = 0;
                if (SearchResultsList.ItemContainerGenerator
                        .ContainerFromIndex(0) is ListBoxItem item)
                {
                    item.Focus();
                }
            });
    }

    private async void OnSearchResultClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                SearchResultsList,
                (DependencyObject)e.OriginalSource) is ListBoxItem item
            && item.DataContext is MetadataCandidate candidate)
        {
            await ApplyCandidateAsync(candidate);
        }
    }

    private async void OnSearchResultsKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || SearchResultsList.SelectedItem is not MetadataCandidate candidate)
        {
            return;
        }
        e.Handled = true;
        await ApplyCandidateAsync(candidate);
    }

    private async Task ApplyCandidateAsync(MetadataCandidate candidate)
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        if (await viewModel.SelectCandidateAsync(candidate))
        {
            TitleBox.Focus();
        }
        else if (viewModel.PendingTvCandidate is not null)
        {
            SeasonNumberBox.Focus();
        }
    }

    private async void OnApplyEpisode(object sender, RoutedEventArgs e) =>
        await ApplyEpisodeAsync();

    private async void OnEpisodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        e.Handled = true;
        await ApplyEpisodeAsync();
    }

    private async Task ApplyEpisodeAsync()
    {
        if (await ((MetadataEditorViewModel)DataContext)
                .ApplyTvEpisodeAsync())
        {
            TitleBox.Focus();
        }
    }

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }
        e.Handled = true;
        ((MetadataEditorViewModel)DataContext).IsSearchPopupOpen = false;
        SearchBox.Focus();
    }

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
        var viewModel = (MetadataEditorViewModel)DataContext;
        if (viewModel.IsSaving)
        {
            e.Cancel = true;
            return;
        }
        viewModel.CancelLookup();
    }
}
