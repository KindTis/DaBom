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

        await FocusFirstItemAsync(SearchResultsList);
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
        else if (viewModel.IsSeasonStep)
        {
            await FocusFirstItemAsync(TvSeasonsList);
        }
        else if (viewModel.IsEpisodeStep)
        {
            await FocusFirstItemAsync(TvEpisodesList);
        }
    }

    private async void OnSeasonClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                TvSeasonsList,
                (DependencyObject)e.OriginalSource) is ListBoxItem item
            && item.DataContext is TvSeasonCandidate season)
        {
            await SelectSeasonAsync(season);
        }
    }

    private async void OnSeasonsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || TvSeasonsList.SelectedItem is not TvSeasonCandidate season)
        {
            return;
        }
        e.Handled = true;
        await SelectSeasonAsync(season);
    }

    private async Task SelectSeasonAsync(TvSeasonCandidate season)
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        await viewModel.SelectSeasonAsync(season);
        if (viewModel.IsEpisodeStep)
        {
            await FocusFirstItemAsync(TvEpisodesList);
        }
    }

    private async void OnEpisodeClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                TvEpisodesList,
                (DependencyObject)e.OriginalSource) is ListBoxItem item
            && item.DataContext is TvEpisodeCandidate episode)
        {
            await SelectEpisodeAsync(episode);
        }
    }

    private async void OnEpisodesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || TvEpisodesList.SelectedItem is not TvEpisodeCandidate episode)
        {
            return;
        }
        e.Handled = true;
        await SelectEpisodeAsync(episode);
    }

    private async Task SelectEpisodeAsync(TvEpisodeCandidate episode)
    {
        if (await ((MetadataEditorViewModel)DataContext).SelectEpisodeAsync(episode))
        {
            TitleBox.Focus();
        }
    }

    private async void OnLookupBack(object sender, RoutedEventArgs e)
    {
        var viewModel = (MetadataEditorViewModel)DataContext;
        viewModel.GoBackInLookup();
        if (viewModel.IsSeasonStep)
        {
            await FocusFirstItemAsync(TvSeasonsList);
        }
        else if (viewModel.IsSearchResultStep)
        {
            await FocusFirstItemAsync(SearchResultsList);
        }
    }

    private async Task FocusFirstItemAsync(ListBox list)
    {
        if (list.Items.Count == 0)
        {
            return;
        }

        await Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                list.SelectedIndex = 0;
                if (list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item)
                {
                    item.Focus();
                }
            });
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
